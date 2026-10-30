# MD301 Plus privacy

MD301 Plus is free to use. It has no ads, subscriptions, in-app purchases,
analytics, or automatic diagnostic uploads. You do not need an app account.

## What stays on your PC

The app saves your chosen trigger, action labels, keyboard shortcuts, and launch
targets in a local `settings.json` file. Launch targets can include app, file,
and folder paths or website URLs that you enter.

When something fails, the app writes a local `app.log` with the error and
diagnostic details. These details can include local paths, configured URLs,
and exception messages. Paths or URLs may contain personal information.
The app does not upload these files or send them to the developer.

The app listens for the trigger you assign and for input needed to operate or
dismiss the ring. It does not record a history of what you type or which apps
you use.

## Websites and other apps

When you choose a launch action, MD301 Plus asks Windows to open its target.
Opening a website uses your browser; the website and browser have their own
privacy practices. The optional ASUS Gear Link setup also opens in your browser.
MD301 Plus does not receive information you enter on those websites.

Microsoft Store account, installation, and update services are provided by
Microsoft and are covered by the
[Microsoft Privacy Statement](https://privacy.microsoft.com/privacystatement).

## Removing local data

The installer and portable versions store settings and logs in
`%LOCALAPPDATA%\MD301Plus`. Exit the app before deleting that folder.
Uninstalling the installer version leaves those files in place so you can keep
your settings if you reinstall.

Windows manages the Store version's private app-data storage and removes that
packaged data when you uninstall it. Data from a separate installer or portable
copy may remain and can be removed separately.

## Support

You can report problems or ask privacy questions through
[GitHub Issues](https://github.com/HugoMSFT/MD301_Plus/issues).
Issues are public. Review screenshots and logs before sharing them, and remove
personal paths, private URLs, or other information you do not want published.
