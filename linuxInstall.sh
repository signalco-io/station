# TODO: Open port 1883 for mosquitto

## Install rpi-update
sudo curl -L --output /usr/bin/rpi-update https://raw.githubusercontent.com/Hexxeh/rpi-update/master/rpi-update && sudo chmod +x /usr/bin/rpi-update
sudo rpi-update

## Install Realtek wifi drivers
sudo wget http://downloads.fars-robotics.net/wifi-drivers/install-wifi -O /usr/bin/install-wifi
sudo chmod +x /usr/bin/install-wifi
sudo install-wifi -c rpi-update
sudo rpi-update
sudo install-wifi -u rpi-update

echo "Setting hostname to 'signalbeacon'"
sudo hostnamectl set-hostname signalbeacon

echo "Configuring firewall"
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow ssh
sudo ufw allow 1883 # Allow MQTT
sudo ufw enable

echo "Updating system..."
sudo bash -c 'for i in update {,dist-}upgrade auto{remove,clean}; do apt-get $i -y; done'

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
echo "Installing git, make, g++, gcc, mosquitto, bluez..."
sudo apt-get install -y git make g++ gcc mosquitto bluez
# sudo apt-get update
# sudo apt-get install -y dotnet-sdk-5.0

# Install .NET 5.0 SDK
sudo snap install dotnet-sdk --classic --channel=5.0/edge
sudo snap alias dotnet-sdk.dotnet dotnet

echo "Cloning Signal.Beacon git repository..."
sudo git clone --single-branch --branch main https://github.com/AleksandarDev/beacon.git  /opt/signalbeacon
sudo chown -R ubuntu:ubuntu /opt/signalbeacon
cd /opt/signalbeacon || exit

# TODO: Build 

echo "Creating service file signalbeacon.service and enableing..."
service_path="/etc/systemd/system/signalbeacon.service"
echo "[Unit]
Description=Signal Beacon
After=network.target
[Service]
ExecStart=/usr/bin/npm start
WorkingDirectory=/opt/signalbeacon
StandardOutput=inherit
StandardError=inherit
Restart=always
User=ubuntu
[Install]
WantedBy=multi-user.target" > $service_path
sudo systemctl enable signalbeacon.service

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

rsync -a 
