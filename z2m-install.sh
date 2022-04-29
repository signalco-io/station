# Root required
if [ $(id -u) != "0" ];
then
	echo -e "|   Error: You need to be root to install the Signalco Zigbee2MQTT integration\n|"
	exit 1
fi

echo "Cloning Zigbee2mqtt git repository..."
sudo git clone --single-branch --branch master https://github.com/Koenkk/zigbee2mqtt.git /opt/zigbee2mqtt
sudo chown -R ubuntu:ubuntu /opt/zigbee2mqtt

echo "Running zigbee2mqtt install... This might take a while and can produce som expected errors"
cd /opt/zigbee2mqtt || exit
npm ci

echo "Creating service file zigbee2mqtt.service and enableing..."
CURRENT_USER=$(whoami)
service_path="/etc/systemd/system/zigbee2mqtt.service"
echo "[Unit]
Description=zigbee2mqtt
After=network.target
[Service]
ExecStart=/usr/bin/npm start
WorkingDirectory=/opt/zigbee2mqtt
StandardOutput=inherit
StandardError=inherit
Restart=always
User=$CURRENT_USER
[Install]
WantedBy=multi-user.target" > $service_path
sudo systemctl enable zigbee2mqtt.service
