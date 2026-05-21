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
    ufw_allow 
}

setup_port(){
    local config_path="$(dirname "$0")/config.json"
    
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

    echo "SELECTED_PORT=$TARGET_PORT"
}

setup_port