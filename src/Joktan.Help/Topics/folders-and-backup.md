# Folders and backup

**Settings / Folders and Backup** shows every folder Joktan keeps things in, and backs them up to a zip file and restores
them from one.

## Changing a folder

1. Find the row under **Folders**.
2. Type a full path, or use **Browse**. `~` stands for your home folder. **Default** fills in Joktan's own folder.
3. Press **Use folder**.

This does the same as the folder buttons on the Notes, MQTT Dashboards and API Client pages, which still work:

- If the folder already holds data, it is opened as it is.
- If not, what is open now is copied there. The originals stay where they were, so nothing is lost.

Some folders cannot be changed here, and their row says why:

- **Certificates**: each new certificate can choose its own folder when you make it. See [Certificates](help:certificates).
- **Passwords**: moving a vault needs it unlocked, so press **Open Passwords**. See [Passwords](help:passwords).

**MQTT messages** has one row for each client on the [MQTT](help:mqtt) page, so you see as many rows as you have clients.
Rows follow the clients as you add, rename and remove them. To give a client no folder, leave the box empty and press
**Use folder**. Its **Back up** switch stays off until it has one.

## Moving the app settings

The **App settings** row says where your MQTT clients, the menu, themes and other settings are kept, along with anything
else in that folder, such as notes you have not moved elsewhere. Choose a folder and press **Use folder and restart**.
Joktan restarts to use it.

- **An empty folder**: everything is copied there, and the originals stay where they were. You can delete the old folder
  once you are happy.
- **A folder that already holds Joktan's settings**: it is opened as it is, and nothing is copied. Use this to share one
  folder between computers, one at a time.
- **A folder with other files in it**: refused, so choose or make an empty one.

**Default** then the button goes back to Joktan's own folder, where any settings you left are opened as they were.

If the folder cannot be found when Joktan starts, such as on a drive that is not connected, Joktan uses its default folder
for that run, opens on this page and says so. Connect the drive and restart to go back. Changes made in the meantime stay
in the default folder.

While Joktan is started with the `JOKTAN_DATA_DIR` environment variable, that sets the folder and this row cannot change it.

## Backing up

1. Turn on the **Back up** switch on each row you want in the backup.
2. Under **Back up**, check the **Backup folder**, and choose how many backups to keep.
3. Press **Back up now**.

Joktan saves what you have open, then makes a dated zip such as `joktan-backup-2026-10-31-140509.zip`. It is an ordinary
zip, so any zip tool can open it. If something could not be read, it is listed under the result.

After each backup, the oldest of Joktan's backups past the number kept are deleted. Other files in that folder are never
touched. Backups only happen when you press the button.

**Keep your backups somewhere safe.** Zips are not encrypted:

- **Certificates** and **MQTT client certificates** hold private keys, so their switches start off, and they warn when turned on.
- **Passwords** in a vault file are safe, as the vault is encrypted with your master password.
- **Passwords in the system keychain, and MQTT passwords, are not in a backup**, as they cannot be read into a file. Keep
  them another way. After restoring on another computer, enter them again.

Files and folders whose names start with a dot, such as `.git`, are left out.

## Restoring

1. Under **Restore**, choose a backup, or press **Browse** to pick one from anywhere.
2. Check which switches are on, and where each will go.
3. Press **Restore**, read what it will replace, and press **Restore and restart**.

A restore makes each folder switched on match the backup, in the place it is kept now. **Anything made since the backup is
removed from those folders.**

Before changing anything, Joktan zips what is there now as `joktan-safety-` and the date, in the backup folder. To undo a
restore, restore that zip. If a backup is damaged, or the safety zip cannot be made, nothing changes.

Joktan restarts to do this, because closing the app saves what it has open. When it is back, this page says what was restored.

## If something goes wrong

- **"is not a Joktan backup"**: the zip was not made here, or has been changed.
- **"was made by a newer Joktan"**: update Joktan, then try again.
- **"The restore did not finish"**: the message says why, and where the safety zip is. Restore that zip to go back.
- **A restored MQTT client cannot find its folder**: this can happen after restoring on another computer. Choose the folder
  on this page.
