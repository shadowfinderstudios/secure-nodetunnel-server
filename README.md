# secure-nodetunnel-server
A secure encrypted version of nodetunnel-server for Godot.

![Secure-Nodetunnel](secure_nt_logo.png?raw=true "Secure Nodetunnel for Godot")

This is the source code for the NodeTunnel relay server. Based on:

https://github.com/curtjs/nodetunnel

https://github.com/curtjs/nodetunnel-server/


It supports authentication tokens, encryption, rate limiting and banning with a time limit. It supports searchable Lobbies. Also, there is now an improved Godot demo project in SecureNodeTunnelExample.

The new Lobby Schema design allows the developer to establish a message contract in the expected parameters
that lobbies will communicate via the lobby registration and search system. This dictionary of parameters
must match on both the server and on the client. And, are present in the HTTP/LobbySchema.cs on the server,
and in nodetunnel/LobbyMetadata.gd in the client plugin. Both client and server MUST match.

In the addons folder is the secure version of the addons plugin for Godot.
In nodetunnel addon make certain to edit the internal/_PacketEncryption.gd and set the variable MASTER_KEY.
Use the NODETUNNEL_MASTER_KEY environment variable while testing so the key doesn't end up in your repo.
When shipping you can hardcode this instead.


### Setup
**This varies a lot depending on your setup.**
1. Setup .NET on your server, this varies depending on your setup. See: https://learn.microsoft.com/en-us/dotnet/core/install/
2. Open ports 9999 for UDP and 9998 for TCP for both incoming and outgoing traffic
3. Clone this repository
4. Build & run the server
   ```dotnet run --configuration Release```
5. The server should now be running! 


Linux specific instructions:

If you want it to run as a daemon, create a /etc/systemd/system/nodetunnel.service
Also, fill in the NODETUNNEL_MASTER_KEY with the master key generated when running the secure nodetunnel the first time.

```
[Unit]
Description=Nodetunnel server
After=syslog.target network.target

[Service]
Environment="NODETUNNEL_MASTER_KEY="
User=nodetunnel
Group=nodetunnel
WorkingDirectory=/home/nodetunnel/nodetunnel
ExecStart=/home/nodetunnel/nodetunnel/NodeTunnel
KillMode=process
Restart=on-failure

[Install]
WantedBy=multi-user.target 
```

Then:
```
add-apt-repository ppa:dotnet/backports
apt update
apt install -y dotnet-sdk-9.0
dotnet --list-sdks

adduser nodetunnel
su nodetunnel
cd /home/nodetunnel
mkdir src
cd src
git clone PUT_THE_NODETUNNEL_SERVER_GITHUB_URL HERE
dotnet publish --configuration Release -p:PublishDir=/home/nodetunnel/nodetunnel
dotnet dev-certs https --trust

As superuser, create the systemd service file I provided and then:
systemctl daemon-reload
systemctl enable nodetunnel.service
systemctl start nodetunnel.service
systemctl status nodetunnel.service

journalctl -u nodetunnel.service -f
```

Now it starts with the system as a daemon and that last command will watch the output as players connect. 

You can confirm the encryption is working:
```
tcpdump -i any -w encrypted.pcap port 9998 or port 9999
ctrl+c to stop when enough data is gathered
```
Connect your client(s) to the secure-nodetunnel. Interact and walk around for a bit.

Now change peer.set_encryption_enabled(true) on the client to false.
And, also turn off the encryption on the server by editing NodeTunnel.cs, changing EnableEncryption and rerunning.

Connect again with your client(s) to the secure-nodetunnel. Interact and walk around for a bit.
```
tcpdump -i any -w unencrypted.pcap port 9998 or port 9999
ctrl+c to stop when enough data is gathered
```

Then run the following to view those packet captures:
```
tcpdump -n -A -r unencrypted.pcap
tcpdump -n -A -r encrypted.pcap
```
And, now notice that the packets are unreadable in the encrypted capture and human readable in the unencrypted capture.

This code is released under the MIT license.

Disclaimer of Liability and Security

THE SOFTWARE IS PROVIDED "AS IS," WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES, OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT, OR OTHERWISE, ARISING FROM, OUT OF, OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE. This includes, but is not limited to, any and all security vulnerabilities, data breaches, or safety issues arising from the use of the nodetunnel project for multiplayer in Godot. You assume all risk associated with the security, reliability, and safety of your application using this project.

This software includes cryptographic features. Users are responsible for complying with all applicable U.S. export control laws and regulations, including the U.S. Export Administration Regulations (EAR). Specifically, this software may not be exported or re-exported to any country or entity to which the U.S. has embargoed goods.
