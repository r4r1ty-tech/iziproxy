#!/bin/bash

set_config_vds(){
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