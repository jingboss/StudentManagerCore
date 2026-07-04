#!/bin/bash
# StudentManagerCore Docker 部署脚本
# 使用方法: bash docker-deploy.sh

set -e

echo "=========================================="
echo " StudentManagerCore Docker 部署"
echo "=========================================="

# 检查 .env 文件
if [ ! -f ".env" ]; then
    echo "[!] 未发现 .env 文件，正在从 .env.example 创建..."
    cp .env.example .env
    echo "[!] 请编辑 .env 文件，修改以下配置："
    echo "    - MYSQL_ROOT_PASSWORD: MySQL 数据库密码"
    echo "    - JWT_SECRET_KEY: JWT 密钥 (至少32位)"
    echo ""
    read -p "按任意键继续编辑 .env 文件..."
    exit 1
fi

echo "[*] 构建并启动容器..."
docker compose down --remove-orphans 2>/dev/null || true
docker compose up -d --build

echo ""
echo "[✓] 部署完成！"
echo ""
echo "    应用地址: http://localhost:5000"
echo "    MySQL 端口: localhost:3307"
echo ""
echo "    首次访问会自动进入安装向导，按提示完成配置即可。"
echo "    安装向导结束后请重启容器：docker compose restart app"
echo ""
