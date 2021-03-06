# Root required
if [ $(id -u) != "0" ];
then
	echo -e "|   Error: You need to be root to install the Signalco Station\n|"
	exit 1
fi

## Download latest station
CURRENT_USER=$(whoami)
echo "Downloading latest stable station..."
URL=$( curl -s "https://api.github.com/repos/signalco-io/station/releases/latest" | jq -r '.assets[] | select(.name | test("beacon-v(.*)-linux-arm64.tar.gz")) | .browser_download_url' )
FILENAME=$( echo $URL | grep -oP "beacon-v\d*.\d*.\d*-linux-arm64" )
curl -LO "$URL"
echo "Extracting station files..."
sudo mkdir -p /opt/signalcostation
sudo tar -xf ./$FILENAME.tar.gz -C /opt/signalcostation
sudo chown -R $CURRENT_USER:$CURRENT_USER /opt/signalcostation
cd /opt/signalcostation || exit

## Stop station
#echo "Stopping station..."
#sudo systemctl stop signalcostation.service

## Configure service
echo "Creating service file signalcostation.service and enabling..."
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
User=$CURRENT_USER
[Install]
WantedBy=multi-user.target" > $service_path
sudo systemctl enable signalcostation.service
sudo systemctl restart signalcostation.service
