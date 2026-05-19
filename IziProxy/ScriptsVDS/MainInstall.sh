#!/bin/bash

install_deps() {
    local distro
    distro=$(grep "^NAME=" /etc/os-release | cut -d '"' -f2)

    declare -A packages=(
        ["Debian GNU/Linux"]="apt-get update && apt-get install -y"
        [Ubuntu]="apt-get update && apt-get install -y"
        [CentOS]="yum install -y"
        [Fedora]="dnf install -y"
        ["Arch Linux"]="pacman -Sy --noconfirm"
        [openSUSE]="zypper install -y"
        ["Alpine Linux"]="apk add --no-cache"
    )

    local cmd="${packages[$distro]}"
    if [[ -z "$cmd" ]]; then
        echo "Дистрибутив '$distro' не поддерживается" >&2
        exit 1
    fi

    echo "Устанавливаем утилиты..."
    $cmd curl unzip wget net-tools ufw
}

enable_bbr() {
    echo "Включаем TCP BBR..."
    echo "net.core.default_qdisc=fq" >> /etc/sysctl.conf
    echo "net.ipv4.tcp_congestion_control=bbr" >> /etc/sysctl.conf
    sysctl -p
}

install_xray() {
    echo "Устанавливаем Xray..."
    bash -c "$(curl -L https://github.com/XTLS/Xray-install/raw/main/install-release.sh)" @ install
}


install_deps
enable_bbr
install_xray

echo "Подготовка VDS завершена"
