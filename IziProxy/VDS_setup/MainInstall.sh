#!/bin/bash

install_deps() {
    local distro_id distro_like
    distro_id=$(grep "^ID=" /etc/os-release | cut -d '=' -f2 | tr -d '"')
    distro_like=$(grep "^ID_LIKE=" /etc/os-release | cut -d '=' -f2 | tr -d '"')

    if [[ "$distro_id" != "debian" && "$distro_id" != "ubuntu" && "$distro_like" != *"debian"* ]]; then
        echo "Поддерживаются только Debian/Ubuntu-подобные дистрибутивы" >&2
        exit 1
    fi

    local cmd="apt-get update && apt-get install -y"

    echo "Устанавливаем утилиты..."
    $cmd curl unzip wget net-tools ufw jq bc
}

enable_bbr() {
    echo "Включаем TCP BBR..."

    grep -q "net.core.default_qdisc=fq" /etc/sysctl.conf || \
        echo "net.core.default_qdisc=fq" >> /etc/sysctl.conf

    grep -q "net.ipv4.tcp_congestion_control=bbr" /etc/sysctl.conf || \
        echo "net.ipv4.tcp_congestion_control=bbr" >> /etc/sysctl.conf

    sysctl -p
    echo "BBR включен"
}

install_xray() {
    echo "Устанавливаем Xray..."
    bash -c "$(curl -L https://github.com/XTLS/Xray-install/raw/main/install-release.sh)" @ install
}

install_deps
enable_bbr
install_xray

echo "Подготовка VDS завершена"
