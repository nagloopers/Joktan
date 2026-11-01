# Certificates

A developer tool for making the certificates that MQTT brokers and clients use for TLS and
mutual TLS: certificate authorities (CAs), and the server and client certificates they sign.
Any number of CAs can be made, and a CA can sign more certificates at any time while it is valid.

| Item      | Value                                   |
|-----------|-----------------------------------------|
| Menu path | Dev Tools / Certificates |
| Status    | Implemented                             |

## Purpose

Let a developer set up TLS for a test broker and its clients without OpenSSL commands: make a
CA, sign a certificate for the broker and one for each client, export the broker's files, and
pick the client's files in its MQTT settings.

## Page layout

1. Page header: title "Certificates", subtitle "Dev Tools / Certificates", and
   **New root CA** on the right.
2. A line saying what happened last, such as which files were exported, and any error.
3. On the left, **Authorities and certificates**: a tree with each root CA at the top and every
   certificate below the CA that signed it. Each entry shows a dot in the colour of its state, its
   name, its kind and its expiry date.
4. On the right, the selected certificate, then where the certificates are kept, and that the
   defaults are on Settings / Certificate Defaults.

## Kinds of certificate

| Kind            | Signed by           | Can sign others | Used for                                                     |
|-----------------|---------------------|-----------------|--------------------------------------------------------------|
| Root CA         | Itself              | Yes             | The top of a chain; brokers and clients trust it by its file |
| Intermediate CA | A root or another intermediate CA | Yes | Signing certificates below a root                     |
| Server          | A CA                | No              | What a broker shows to clients (TLS server authentication)   |
| Client          | A CA                | No              | What a client shows to a broker that asks (mutual TLS)       |

A CA can be made at any time with **New root CA**, and as many as wanted. Selecting a CA shows
**Server certificate**, **Client certificate** and **Intermediate CA**, which open the new
certificate window with that kind chosen and the CA as the signer. A certificate always stays
linked to the CA that signed it: it is shown below it, its files include that CA's certificates,
and it is deleted with it.

## New certificate window

The window's subtitle names what signs the certificate. **Make certificate** is enabled once
everything is valid; the reason is shown beside it otherwise.

| Setting               | Default                                                       | Rules                                                                                     |
|-----------------------|---------------------------------------------------------------|-------------------------------------------------------------------------------------------|
| Kind                  | The button pressed; Root CA from **New root CA**              | Server, client or intermediate CA when a CA signs it; only root CA otherwise              |
| Name in the list      | Empty, meaning the common name                                | Up to 64 characters; unique, ignoring case. Left empty, a taken name gets " (2)" and so on |
| Folder                | Root CA: the folder the last root CA was made in, at first `certificates/` in Joktan's data folder. Signed by a CA: the folder that CA was made in | A full path, typed or chosen with **Browse**. It is made if it does not exist |
| Common name (CN)      | The default for the kind                                      | Required; up to 64 characters                                                             |
| Organisation (O)      | The signing CA's, else the default                            | Up to 128 characters                                                                      |
| Organisational unit (OU) | The signing CA's, else the default                         | Up to 128 characters                                                                      |
| Locality (L)          | The signing CA's, else the default                            | Up to 128 characters                                                                      |
| State or province (ST) | The signing CA's, else the default                           | Up to 128 characters                                                                      |
| Country (C)           | The signing CA's, else the default                            | Empty or two letters; written in capitals                                                 |
| Email address         | Empty                                                         | Optional                                                                                  |
| Alternative names     | Server: the default host names; client: empty                 | Host names (a leading `*.` is allowed), IP addresses and email addresses, separated by commas or spaces. A server certificate needs at least one. Not shown for a CA |
| Key                   | The default                                                   | ECDSA P-256, ECDSA P-384, RSA 2048 or RSA 4096                                            |
| Expires on            | Today plus the default number of days for the kind            | After today, and not after the day the signing CA expires                                 |

Beside the expiry date are **1 year**, **2 years**, **5 years** and **10 years**, which set it
from today; a date past the signing CA's expiry is brought back to that day. Below it the window
shows how many days from today that is.

The window shows the folder the files will go in: a new folder inside the chosen one, named
after the certificate in lower case with dashes, such as `joktan-root-ca` or `localhost`, and
with `-2`, `-3` and so on added when that name is taken.

Changing the kind changes the common name, alternative names and expiry date to that kind's
defaults, unless they were changed by hand.

What is made:

- The certificate is valid from five minutes ago, allowing for clocks that are a little behind,
  until the end of the chosen day by the computer's clock. It is never valid outside the time
  its CA is.
- A CA is marked as a CA, and may sign certificates and revocation lists. A server certificate
  is for TLS server authentication and a client certificate for TLS client authentication;
  neither can sign others.
- Every certificate has a random 128-bit serial number and identifies its own key and its CA's.
- It is signed with SHA-256, or SHA-384 when the signing key is ECDSA P-384 or RSA 4096.

## Selected certificate

| Part                 | What it shows                                                                                       |
|----------------------|-----------------------------------------------------------------------------------------------------|
| Title                | Its name, kind, and how long it has left: "Valid for N more days", "Expires in N days", "Expired on …" |
| Export and Delete    | See below                                                                                          |
| Sign a new certificate with this CA | For a CA only: **Server certificate**, **Client certificate**, **Intermediate CA**  |
| Name in the list     | With **Rename**. The name is only Joktan's; the certificate does not change                       |
| Subject              | The full name in the certificate                                                                   |
| Signed by            | The CA's name in the list and its subject; "Itself" for a root CA                                  |
| Valid                | From and to, to the minute                                                                         |
| Used for             | Signs certificates, TLS server authentication or TLS client authentication                        |
| Alternative names    | For a server or client certificate                                                                 |
| Key, serial number, SHA-256 fingerprint | As in the certificate                                                           |
| Used by              | The MQTT clients whose TLS settings use one of its files, or "No MQTT client"                     |
| Files                | The paths of its files, with **Open folder**                                                       |

The state dot is green while valid, amber within 30 days of expiring and red once expired or when
its file cannot be read. A CA that has expired, or whose key is missing, cannot sign new
certificates; trying says why.

## Files

Each certificate has a folder of its own, inside the folder chosen when it was made (see the
new certificate window). Certificates made before the folder could be chosen are under
`certificates/` in Joktan's data folder, the one that holds `mqtt.json`, in folders named by an
internal ID. A certificate's folder holds:

| File        | What it is                                                                                                      |
|-------------|-----------------------------------------------------------------------------------------------------------------|
| `cert.pem`  | The certificate                                                                                                 |
| `key.pem`   | Its private key, PKCS #8 PEM, not encrypted                                                                     |
| `chain.pem` | The certificate followed by the intermediate CAs above it: what a broker should show, its `certfile`          |
| `ca.pem`    | The CA certificates to trust it by, nearest first and the root last: for a CA, itself and the CAs above it; for a server or client certificate, the CAs above it. For a `cafile` |

`certificates.json`, beside `mqtt.json`, holds the list: each certificate's name, kind, folder,
the CA that signed it and when it was made, as well as the defaults, the folder of the last root
CA and the last export folder. The page shows where it is. The
rest is read from the certificate files.

On Linux and macOS each certificate's folder and key file can be read only by the user. A
chosen folder that does not exist is made with the usual permissions. The keys are not
encrypted, so anyone who can read them can use them, and anyone with a CA's key can make
certificates that brokers and clients trusting that CA accept. The page says so at the bottom.

## Export

**Export** writes the selected certificate into a folder, for a broker or another program.

| Setting                         | Default                                                    | Meaning                                                                    |
|---------------------------------|------------------------------------------------------------|----------------------------------------------------------------------------|
| Folder                          | The folder of the last export                              | Must exist; **Browse** picks one                                           |
| File name                       | `server` or `client`; for a CA, its name in lower case     | Without an extension                                                       |
| Private key                     | On, except for a CA                                        | `name.key`, not encrypted                                                  |
| CA certificates                 | On; not shown for a root CA                                | `ca.crt`, the same as `ca.pem`                                             |
| Intermediate CAs in `name.crt`  | On for a server certificate; shown only below an intermediate CA | `name.crt` holds the certificate followed by the intermediate CAs    |
| PFX/P12 file                    | Off                                                        | `name.p12` with the certificate, its key and the CAs above it, protected by a password that must be entered |
| Replace files of the same name  | Off                                                        | Otherwise the export stops and names the files already there               |

The window lists the files that will be written. Afterwards the page says which files were
written and where. The PFX/P12 file uses AES-256 and SHA-256, which OpenSSL 1.1 and later, and
current Windows and macOS, can read.

## Delete

**Delete** asks for confirmation, saying how many certificates below it go too. It deletes the
certificate and everything it signed: their four files, and each one's folder once it is empty.
Other files in those folders are left alone. MQTT clients that used their files then fail
to connect with "The … file … does not exist".

## Defaults

The **Settings / Certificate Defaults** page sets what a new certificate is filled in with. Each
change is saved as soon as the values are valid; an invalid value is named under the card and not
saved. **Reset** returns to the built-in defaults.

| Default                             | Built in                    |
|-------------------------------------|-----------------------------|
| Root CA common name                 | `Joktan Root CA`            |
| Intermediate CA common name         | `Joktan Intermediate CA`    |
| Server common name                  | `localhost`                 |
| Client common name                  | `joktan-client`             |
| Server host names and IP addresses  | `localhost, 127.0.0.1, ::1` |
| Organisation (O)                    | `Joktan`                    |
| Organisational unit (OU)            | `Development`               |
| Locality, state, country            | Empty                       |
| Key                                 | ECDSA P-256                 |
| CAs valid for                       | 3650 days                   |
| Certificates valid for              | 825 days                    |
| Folder for MQTT clients' certificates | Empty, meaning `mqtt-certificates/` in Joktan's data folder, shown as the placeholder |

Numbers of days are from 1 to 36500. The folder for MQTT clients' certificates must be a full
path, typed or chosen with **Browse**; it is made when first needed. Certificates default to 825 days because Apple's systems
refuse TLS server certificates valid for longer.

## Use with MQTT

- **Clients.** In a client's settings on the [MQTT page](../mqtt/spec.md), **Or a CA from
  Certificates** picks a CA, and **Or one from Certificates** picks a client certificate, with
  its signing CA when no CA was chosen. Picking a file another way, with **Browse** or by typing,
  clears the choice.

  When the client is saved with TLS on, what was picked is copied into a folder of its own, named
  after the client in lower case with dashes, inside the folder for MQTT clients' certificates
  (see [Defaults](#defaults)):

  | File         | Copied from                                         |
  |--------------|-----------------------------------------------------|
  | `ca.crt`     | The CA's `ca.pem`: the CA and the CAs above it      |
  | `client.crt` | The client certificate's `cert.pem`                 |
  | `client.key` | Its `key.pem`; on Linux and macOS readable only by the user |

  The client uses the copies, so it never reads the folders holding the CAs' keys, and keeps
  working if the certificates are deleted from this page. Saving again copies them again,
  replacing the files. The client also remembers which certificates the copies came from: its
  settings show them picked, and **Used by** names the client. A copy that cannot be written
  stops the save and says why. The folder is not removed with the client.
- **Brokers.** Export the broker's server certificate with its key and CAs, and give the broker
  `name.crt` as its certificate, `name.key` as its key and `ca.crt` as its CA file. For mutual
  TLS, the client certificates must be signed by a CA in that file. The local broker in
  `containers/` uses a server certificate called `localhost` made in `containers/certs/joktan`
  without exporting; see [containers/README.md](../../../../../containers/README.md#certificates-from-certs).

## Out of scope

- Revocation lists and OCSP.
- Importing CAs made elsewhere, and signing certificate requests (CSRs).
- Encrypting the stored keys with a password.
- Renewing a certificate in place; make a new one from the same CA instead.
