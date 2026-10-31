# Clients and brokers

A client is one connection to one broker. The first time Joktan starts there is one client, `main`,
for `localhost` port 1883.

- **Add client** adds a client, on a tab of its own.
- **Edit client** changes one. It is available only while the client is disconnected.
- **Remove client** removes the client and its tab, also only while disconnected. The last client
  cannot be removed. Its folder and message files are left as they are.

The server for alerts and messages is set up under
[Settings / Alerts and Messages](help:alerts-and-messages). It has a tab here only if you switch that on.

## Broker settings

| Setting | Notes |
| --- | --- |
| Name | Required and unique. Up to 40 characters |
| Client ID | No spaces. Left empty, one is made up |
| Host | Required. An IP address or host name |
| Port | Required. 1 to 65535; `1883` by default |
| Username | Optional. Sent only when filled in |
| Password | Optional. Needs a username |

**Save** is enabled once the settings are valid; otherwise the reason is shown beside it.

Passwords are not written to Joktan's settings file. They are kept in the operating system's
credential store, after a successful connection. See
[Settings and data](help:settings-and-data).

## TLS

The **TLS** section encrypts the connection and sets its certificates. See
[TLS and mutual TLS](help:mqtt-tls).

## Appearance

Colours help tell clients apart, on this page and in the Topic Explorer: **Tab text**, **Tab fill**
and **Page background**. Pick one from the palette, type one such as `#3BB001`, or press **Default**
for the theme's. **Preview** shows the result.

## Connecting

**Connect** is enabled once the host and port are valid. Joktan never connects by itself when it starts.

| State | Meaning |
| --- | --- |
| Disconnected | No connection, and none being attempted |
| Connecting | The first attempt is in progress |
| Connected | Ready to send |
| Reconnecting | The connection dropped and is being retried |
| Failed | The last attempt did not succeed; the reason is shown |

A connection attempt fails after 10 seconds without an answer. If an established connection drops,
Joktan tries again every 5 seconds until it succeeds or you press **Disconnect**. Disconnecting a client
stops its scheduled messages; other clients carry on.

## When it will not connect

- **Connection refused**: nothing is listening on that host and port. Check that the broker is running,
  on the [Containers](help:containers) page if it is local.
- **Not authorized**: the broker does not accept the username and password.
- **A certificate problem**: see [TLS and mutual TLS](help:mqtt-tls).
