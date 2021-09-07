# Create SWAP file
echo "Configuring SWAP file..."
sudo fallocate -l 1G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
sudo cp /etc/fstab /etc/fstab.bak
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab

## Configure hostname
echo "Setting hostname to 'signalcostation'..."
sudo hostnamectl set-hostname signalcostation

## Configure firewall
echo "Configuring firewall..."
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow ssh
sudo ufw allow 1883 # Allow MQTT
sudo ufw allow 8080 # Allow Z2M UI (Optional)
sudo ufw allow 80 # Allow Station UI
sudo ufw enable

## Housekeeping
echo "Updating system..."
sudo bash -c 'for i in update {,dist-}upgrade auto{remove,clean}; do apt-get $i -y; done'

### Disable snap updates (metered connection) and do update now
echo "Disableing SNAP updates..."
sudo snap set system refresh.metered=hold
echo "Updating snaps..."
sudo snap refresh

## Install node
echo "Checking if NodeJS is installed..."
node=$(which npm)
if [ -z "${node}" ]; then #Installing NodeJS if not already installed.
  printf "Downloading and installing NodeJS...\\n"
  curl -sL https://deb.nodesource.com/setup_14.x | bash -
  sudo apt install -y nodejs npm
fi

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
sudo git clone --single-branch --branch master https://github.com/Koenkk/zigbee2mqtt.git /opt/zigbee2mqtt
sudo chown -R ubuntu:ubuntu /opt/zigbee2mqtt

echo "Running zigbee2mqtt install... This might take a while and can produce som expected errors"
cd /opt/zigbee2mqtt || exit
npm ci

echo "Creating service file zigbee2mqtt.service and enableing..."
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
User=ubuntu
[Install]
WantedBy=multi-user.target" > $service_path
sudo systemctl enable zigbee2mqtt.service
