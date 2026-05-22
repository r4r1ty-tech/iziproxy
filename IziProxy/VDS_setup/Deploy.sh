#!/bin/bash
## Функция для будующего масштабирования
check_ufw(){
    ufw_allow "$@"
}

ufw_allow(){
    local selected_port="$1"
    local previous_port="$2"

    if ! command -v ufw >/dev/null 2>&1; then
        echo "ufw not found, skip firewall setup"
        return 0
    fi

    ufw allow OpenSSH >/dev/null 2>&1 || ufw allow 22/tcp >/dev/null 2>&1

    if ufw status | grep -qi "inactive"; then
        ufw --force enable >/dev/null 2>&1
    fi

    if [ -n "$selected_port" ]; then
        ufw --force delete deny "${selected_port}/tcp" >/dev/null 2>&1 || true
        ufw allow "${selected_port}/tcp" >/dev/null 2>&1
    fi

    declare -A close_ports
    if [ -n "$previous_port" ] && [ "$previous_port" != "$selected_port" ]; then
        close_ports["$previous_port"]=1
    fi

    for p in 443 8443; do
        if [ "$p" != "$selected_port" ]; then
            close_ports["$p"]=1
        fi
    done

    for p in "${!close_ports[@]}"; do
        ufw --force delete allow "${p}/tcp" >/dev/null 2>&1 || true
        ufw deny "${p}/tcp" >/dev/null 2>&1 || true
    done

    ufw reload >/dev/null 2>&1 || true
}

have_jq(){
    command -v jq >/dev/null 2>&1
}

warn_no_jq(){
    if [ -z "${JQ_WARNED:-}" ]; then
        echo "jq not found, skip config read/update"
        JQ_WARNED=1
    fi
}

setup_port(){
    local config_path="$(dirname "$0")/config.json"
    local previous_port=""
    local can_jq=1

    if ! have_jq; then
        can_jq=0
    fi

    if [ -f "$config_path" ] && [ "$can_jq" -eq 1 ]; then
        previous_port=$(jq -r '.inbounds[0].port // empty' "$config_path")
    elif [ -f "$config_path" ]; then
        warn_no_jq
    fi
    
    if ! ss -tuln | grep -q ":443 "; then
        TARGET_PORT=443
    elif ! ss -tuln | grep -q ":8443 "; then
        TARGET_PORT=8443
    else
        while true; do
            TARGET_PORT=$(shuf -i 10000-50000 -n 1)
            if ! ss -tuln | grep -q ":$TARGET_PORT "; then
                break
            fi
        done
    fi

    if [ -f "$config_path" ] && [ "$can_jq" -eq 1 ]; then
        jq ".inbounds[0].port = $TARGET_PORT" "$config_path" > "${config_path}.tmp" && mv "${config_path}.tmp" "$config_path"
    elif [ -f "$config_path" ]; then
        warn_no_jq
    fi

    ufw_allow "$TARGET_PORT" "$previous_port"

    echo "SELECTED_PORT=$TARGET_PORT"
}

is_public_ip(){
    local ip="$1"

    if [ -z "$ip" ]; then
        return 1
    fi

    if [[ "$ip" == *:* ]]; then
        case "$ip" in
            ::1|::|fe80:*|fc*|fd*) return 1 ;;
        esac
        return 0
    fi

    case "$ip" in
        0.*|10.*|127.*|169.254.*|192.168.*) return 1 ;;
        172.1[6-9].*|172.2[0-9].*|172.3[0-1].*) return 1 ;;
        100.6[4-9].*|100.[7-9]*|100.1[0-1]*|100.12[0-7].*) return 1 ;;
        192.0.2.*|198.51.100.*|203.0.113.*) return 1 ;;
    esac

    return 0
}

sni_probe_domain(){
    local domain="$1"
    local samples="${SNI_SAMPLES:-2}"
    local connect_list="" app_list="" total_list=""
    local ok_h2=0 ok_code=0
    local ip=""
    local i=1
    local curl_http2_args=""

    if ! command -v curl >/dev/null 2>&1; then
        return 1
    fi

    if [ "${SNI_CURL_HTTP2:-1}" = "1" ]; then
        curl_http2_args="--http2"
    fi

    while [ "$i" -le "$samples" ]; do
        local out="" connect="" app="" total="" httpver="" code="" ip_cur=""
        out="$(
            curl -o /dev/null -sS \
                --connect-timeout 4 \
                --max-time 8 \
                $curl_http2_args \
                -w 'connect=%{time_connect} app=%{time_appconnect} total=%{time_total} httpver=%{http_version} code=%{http_code} ip=%{remote_ip}\n' \
                "https://${domain}" 2>/dev/null || true
        )"

        connect="$(awk -F'[ =]' '{for(i=1;i<=NF;i++) if($i=="connect") {print $(i+1); exit}}' <<<"$out")"
        app="$(awk -F'[ =]' '{for(i=1;i<=NF;i++) if($i=="app") {print $(i+1); exit}}' <<<"$out")"
        total="$(awk -F'[ =]' '{for(i=1;i<=NF;i++) if($i=="total") {print $(i+1); exit}}' <<<"$out")"
        httpver="$(awk -F'[ =]' '{for(i=1;i<=NF;i++) if($i=="httpver") {print $(i+1); exit}}' <<<"$out")"
        code="$(awk -F'[ =]' '{for(i=1;i<=NF;i++) if($i=="code") {print $(i+1); exit}}' <<<"$out")"
        ip_cur="$(awk -F'[ =]' '{for(i=1;i<=NF;i++) if($i=="ip") {print $(i+1); exit}}' <<<"$out")"

        if [ -n "$ip_cur" ]; then
            ip="$ip_cur"
        fi

        if [ -n "$connect" ]; then
            connect_list="$connect_list $connect"
        fi
        if [ -n "$app" ]; then
            app_list="$app_list $app"
        fi
        if [ -n "$total" ]; then
            total_list="$total_list $total"
        fi

        if [ "$httpver" = "2" ]; then
            ok_h2=1
        fi
        if [ -n "$code" ] && [ "$code" != "000" ]; then
            ok_code=1
        fi

        i=$((i+1))
    done

    if [ -z "$connect_list" ] || [ -z "$total_list" ]; then
        return 1
    fi

    local connect_avg="" app_avg="" total_avg=""
    connect_avg="$(awk '{sum=0; for(i=1;i<=NF;i++) sum+=$i; printf "%.6f", sum/NF}' <<<"$connect_list")"
    total_avg="$(awk '{sum=0; for(i=1;i<=NF;i++) sum+=$i; printf "%.6f", sum/NF}' <<<"$total_list")"
    if [ -n "$app_list" ]; then
        app_avg="$(awk '{sum=0; for(i=1;i<=NF;i++) sum+=$i; printf "%.6f", sum/NF}' <<<"$app_list")"
    else
        app_avg="$total_avg"
    fi

    echo "${connect_avg}|${app_avg}|${total_avg}|${ok_h2}|${ok_code}|${ip}"
}

score_sni_domain(){
    local domain="$1"
    local probe=""
    local connect="" app="" total="" ok_h2="" ok_code="" ip=""
    local ok_tls=0 ok_verify=0
    local score=""

    probe="$(sni_probe_domain "$domain")" || return 1
    IFS='|' read -r connect app total ok_h2 ok_code ip <<<"$probe"

    if ! is_public_ip "$ip"; then
        return 1
    fi

    if command -v openssl >/dev/null 2>&1 && command -v timeout >/dev/null 2>&1; then
        local verify_arg=""
        if [ "${SNI_OPENSSL_VERIFY_HOSTNAME:-0}" = "1" ]; then
            verify_arg="-verify_hostname ${domain}"
        fi
        local openssl_out=""
        openssl_out="$(
            timeout 8 openssl s_client \
                -connect "${domain}:443" \
                -servername "${domain}" \
                -tls1_3 $verify_arg < /dev/null 2>/dev/null || true
        )"
        if grep -qE 'Protocol *: TLSv1\.3|TLSv1\.3' <<<"$openssl_out"; then
            ok_tls=1
        fi
        if grep -q 'Verify return code: 0' <<<"$openssl_out"; then
            ok_verify=1
        fi
    fi

    score="$(awk -v c="$connect" -v a="$app" -v t="$total" -v tls="$ok_tls" -v h2="$ok_h2" -v code="$ok_code" -v vfy="$ok_verify" '
        BEGIN {
            s = 100
            if (c < 999) s -= (c * 120)
            if (a < 999) s -= (a * 50)
            if (t < 999) s -= (t * 15)
            s += (tls * 20)
            s += (h2 * 10)
            s += (code * 5)
            s += (vfy * 10)
            if (s < 0) s = 0
            printf "%.0f", s
        }'
    )"

    echo "${score}|${connect}|${app}|${total}|${ok_tls}|${ok_h2}|${ok_code}|${ok_verify}|${ip}"
}

select_best_sni(){
    local domain_file="${1:-}"
    local config_path="$(dirname "$0")/config.json"
    local -a domains=()
    local best=""
    local best_score=-1
    local can_jq=1

    if ! have_jq; then
        can_jq=0
    fi

    if ! command -v curl >/dev/null 2>&1; then
        echo "SNI selection skipped (curl not found)"
        return 0
    fi

    if curl -V 2>/dev/null | grep -qi 'http2'; then
        SNI_CURL_HTTP2=1
    else
        SNI_CURL_HTTP2=0
    fi

    if command -v openssl >/dev/null 2>&1; then
        if openssl s_client -help 2>&1 | grep -q -- '-verify_hostname'; then
            SNI_OPENSSL_VERIFY_HOSTNAME=1
        fi
    fi

    if [ -n "$domain_file" ] && [ -f "$domain_file" ]; then
        mapfile -t domains < <(grep -vE '^\s*#|^\s*$' "$domain_file" | awk '{print $1}')
    fi

    if [ -f "$config_path" ] && [ "$can_jq" -eq 1 ]; then
        local dest=""
        dest="$(jq -r '.inbounds[0].streamSettings.realitySettings.dest // empty' "$config_path")"
        if [ -n "$dest" ]; then
            domains+=("${dest%%:*}")
        fi
        while IFS= read -r sni; do
            [ -n "$sni" ] && domains+=("$sni")
        done < <(jq -r '.inbounds[0].streamSettings.realitySettings.serverNames[]? // empty' "$config_path")
    elif [ -f "$config_path" ]; then
        warn_no_jq
    fi

    if [ "${#domains[@]}" -eq 0 ]; then
        domains=(
            speed.cloudflare.com
            cdn.jsdelivr.net
            cdnjs.cloudflare.com
            www.cloudflare.com
            static.cloudflareinsights.com
            fonts.gstatic.com
            ajax.cloudflare.com
            assets.adobedtm.com
            www.microsoft.com
            www.apple.com
        )
    fi

    declare -A uniq=()
    local -a filtered=()
    for d in "${domains[@]}"; do
        [ -z "$d" ] && continue
        if [ -z "${uniq[$d]+x}" ]; then
            uniq[$d]=1
            filtered+=("$d")
        fi
    done
    domains=("${filtered[@]}")

    for d in "${domains[@]}"; do
        local result="" score="" connect="" app="" total="" ok_tls="" ok_h2="" ok_code="" ok_verify="" ip=""
        result="$(score_sni_domain "$d")" || continue
        IFS='|' read -r score connect app total ok_tls ok_h2 ok_code ok_verify ip <<<"$result"
        if [[ "$score" =~ ^[0-9]+$ ]] && [ "$score" -gt "$best_score" ]; then
            best_score="$score"
            best="$d"
        fi
        if [ "${SNI_VERBOSE:-0}" = "1" ]; then
            echo "SNI_SCORE domain=$d score=$score connect=$connect app=$app total=$total tls13=$ok_tls h2=$ok_h2 verify=$ok_verify ip=$ip"
        fi
    done

    if [ -z "$best" ]; then
        echo "SNI selection failed"
        return 1
    fi

    if [ -f "$config_path" ] && [ "$can_jq" -eq 1 ]; then
        jq --arg sni "$best" \
            '.inbounds[0].streamSettings.realitySettings.dest = ($sni + ":443")
             | .inbounds[0].streamSettings.realitySettings.serverNames = [$sni]' \
            "$config_path" > "${config_path}.tmp" && mv "${config_path}.tmp" "$config_path"
    elif [ -f "$config_path" ]; then
        warn_no_jq
    fi

    echo "SNI_SELECTED=$best"
}



setup_port
select_best_sni "${SNI_DOMAIN_FILE:-}"