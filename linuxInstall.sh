# TODO: Open port 1883 for mosquitto

## Disable snap updates (metered connection)
sudo snap set system refresh.metered=hold

## Configure hostname
echo "Setting hostname to 'signalcostation'"
sudo hostnamectl set-hostname signalcostation

## Configure firewall
echo "Configuring firewall"
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow ssh
sudo ufw allow 1883 # Allow MQTT
sudo ufw enable

## Housekeeping
echo "Updating system..."
sudo bash -c 'for i in update {,dist-}upgrade auto{remove,clean}; do apt-get $i -y; done'
sudo snap refresh

node=$(which npm)
if [ -z "${node}" ]; then #Installing NodeJS if not already installed.
  printf "Downloading and installing NodeJS...\\n"
  curl -sL https://deb.nodesource.com/setup_14.x | bash -
  sudo apt install -y nodejs npm
fi

# echo "Adding Microsoft Ubuntu 20.10 package signing key to list of trusted keys and add the package repository..."
# wget https://packages.microsoft.com/config/ubuntu/20.10/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
# sudo dpkg -i packages-microsoft-prod.deb

# Install prerequesites
echo "Installing dependencies..."
sudo apt-get install -y make g++ gcc bluez jq

## Download latest station
echo "Downloading latest stable station..."
URL=$( curl -s "https://api.github.com/repos/signalco-io/station/releases/latest" | jq -r '.assets[] | select(.name | test("beacon-v(.*)-linux-arm64.tar.gz")) | .browser_download_url' )
FILENAME=$( echo $URL | grep -oP "beacon-v\d*.\d*.\d*-linux-arm64" )
curl -LO "$URL"
sudo mkdir /opt/signalcostation
sudo tar -xf ./$FILENAME.tar.gz -C /opt/signalcostation
sudo chown -R ubuntu:ubuntu /opt/signalcostation
cd /opt/signalcostation || exit

## Configure service
## TODO: Test if $FILENAME is valid in echo
echo "Creating service file signalcostation.service and enableing..."
service_path="/etc/systemd/system/signalcostation.service"
echo "[Unit]
Description=Signal Station
After=network.target
[Service]
ExecStart=/opt/signalcostation/$FILENAME/Signal.Beacon
WorkingDirectory=/opt/signalcostation/$FILENAME
StandardOutput=inherit
StandardError=inherit
Restart=always
User=ubuntu
[Install]
WantedBy=multi-user.target" > $service_path
sudo systemctl enable signalcostation.service

echo "Cloning dev branch of Zigbee2mqtt git repository..."
sudo git clone --single-branch --branch dev https://github.com/Koenkk/zigbee2mqtt.git  /opt/zigbee2mqttdev
sudo chown -R ubuntu:ubuntu /opt/zigbee2mqttdev

echo "Running zigbee2mqtt install... This might take a while and can produce som expected errors"
cd /opt/zigbee2mqttdev || exit
npm ci

echo "Creating service file zigbee2mqttdev.service and enableing..."
service_path="/etc/systemd/system/zigbee2mqttdev.service"
echo "[Unit]
Description=zigbee2mqttdev
After=network.target
[Service]
ExecStart=/usr/bin/npm start
WorkingDirectory=/opt/zigbee2mqttdev
StandardOutput=inherit
StandardError=inherit
Restart=always
User=ubuntu
[Install]
WantedBy=multi-user.target" > $service_path
sudo systemctl enable zigbee2mqttdev.service

#rsync -a 
