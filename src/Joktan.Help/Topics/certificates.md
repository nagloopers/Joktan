# Certificates

Makes the certificates that MQTT brokers and clients use for TLS and mutual TLS, without OpenSSL commands:
certificate authorities (CAs), and the server and client certificates they sign.

## Set up TLS from scratch

1. Press **New root CA**, then **Make certificate**.
2. Select the CA, and press **Server certificate** to make one for the broker. Check that **Alternative
   names** lists every name and address clients will connect to.
3. For mutual TLS, press **Client certificate** on the CA, once for each client.
4. Select the server certificate, press **Export**, and give the broker the files. See below.
5. In the client's settings on the MQTT page, choose the CA, and the client certificate if there is one.
   See [TLS and mutual TLS](help:mqtt-tls).

## Kinds of certificate

| Kind | Signed by | Used for |
| --- | --- | --- |
| Root CA | Itself | The top of a chain |
| Intermediate CA | Another CA | Signing certificates below a root |
| Server | A CA | What a broker shows to clients |
| Client | A CA | What a client shows to a broker |

Make as many as you need. A certificate stays linked to the CA that signed it: it is listed below it, and
deleted with it.

## Making a certificate

Select a CA and press **Server certificate**, **Client certificate** or **Intermediate CA**. Only **New root
CA** makes a certificate with nothing above it.

- **Name in the list**: only Joktan's name for it. Empty means the common name. **Rename** changes it later.
- **Folder**: where its files go, in a new folder inside the one chosen, made if it does not exist. A signed
  certificate starts with the CA's folder.
- **Common name (CN)** is required. The rest of the subject is filled in from the CA and the defaults.
- **Alternative names**: host names (a leading `*.` is allowed), IP addresses and email addresses, separated
  by commas or spaces. A server certificate needs at least one.
- **Key**: ECDSA P-256 or P-384, or RSA 2048 or 4096.
- **Expires on**: **1 year**, **2 years**, **5 years** and **10 years** set it from today. A certificate
  cannot outlast the CA that signs it.

**Make certificate** is enabled once everything is valid; otherwise the reason is shown beside it. An expired
CA, or one whose key is missing, cannot sign.

## The selected certificate

The dot beside each one is green while valid, amber within 30 days of expiring, and red once expired or when
its file cannot be read. **Used by** names the MQTT clients using it, and **Open folder** shows its files:

- `cert.pem`: the certificate.
- `key.pem`: its private key, not encrypted.
- `chain.pem`: the certificate and the intermediate CAs above it, for a broker's `certfile`.
- `ca.pem`: the CA certificates to trust it by, for a `cafile`.

Certificates cannot be renewed in place. Make a new one from the same CA instead.

## Exporting for a broker

**Export** writes the selected certificate into a folder, under the **File name** you give.

- `name.crt` is the certificate. **Put the intermediate CAs after the certificate** adds them to it.
- **Private key** writes `name.key`, not encrypted.
- **CA certificates** writes `ca.crt`.
- **PFX/P12 file** writes `name.p12`, with the certificate, key and CAs, under a password you give.
- **Replace files of the same name**: otherwise the export stops, and names the files already there.

Give the broker `name.crt` as its certificate, `name.key` as its key and `ca.crt` as its CA file. For mutual
TLS, client certificates must be signed by a CA in that file.

## Deleting

**Delete** deletes a certificate and everything it signed, once you confirm: their four files, and each one's
folder once it is empty. MQTT clients that picked them here keep their own copies.

## Configuring the defaults

The Settings / Certificate Defaults page sets what a new certificate is filled in with. Each change is saved
as soon as it is valid; an invalid value is named under the card. **Reset** returns to the built-in ones.

- The common name for each kind, and **Server host names and IP addresses**.
- **Organisation**, **Organisational unit**, **Locality**, **State or province** and **Country**. A certificate
  signed by a CA takes these from the CA where it has them.
- **Key**: ECDSA P-256 to start with.
- **CAs valid for (days)**: 3650 to start with.
- **Certificates valid for (days)**: 825 to start with, because Apple's systems refuse TLS server certificates
  valid for longer.
- **Folder for MQTT clients' certificates**: where certificates picked for a client are copied when it is saved.

## Keep the keys safe

Private keys are stored unencrypted; on Linux and macOS only you can read them. Anyone with a CA's key can make
certificates that clients and brokers trusting that CA accept. These certificates are meant for development and
testing.

Not available: revocation lists, importing CAs made elsewhere, and signing certificate requests (CSRs).
