#!/bin/bash
## Функция для будующего масштабирования
get_config_vds(){
    CORES=$(nproc)
    
    RAM_GB=$(awk '/MemTotal/ {print int($2/1024/1024 + 0.5)}' /proc/meminfo)
    
    # Если памяти меньше 1 ГБ, считаем как 1
    if [ "$RAM_GB" -lt 1 ]; then
        RAM_GB=1
    fi
    
    VDS_VALUE=$((CORES + RAM_GB))
}
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

setup_port(){
    local config_path="$(dirname "$0")/config.json"
    local previous_port=""

    if [ -f "$config_path" ]; then
        previous_port=$(jq -r '.inbounds[0].port // empty' "$config_path")
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

    if [ -f "$config_path" ]; then
        jq ".inbounds[0].port = $TARGET_PORT" "$config_path" > "${config_path}.tmp" && mv "${config_path}.tmp" "$config_path"
    fi

    ufw_allow "$TARGET_PORT" "$previous_port"

    echo "SELECTED_PORT=$TARGET_PORT"
}

setup_port