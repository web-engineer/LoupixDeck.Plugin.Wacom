# LoupixDeck.Plugin.Wacom

Loupedeck keys for two Wacom driver functions that otherwise only an ExpressKey (or an ExpressKey
Remote) bound to them can trigger:

| Command | Wacom function |
|---|---|
| `Wacom.DisplayToggle` | Display Toggle: move the pen to the next display set up in Wacom Center |
| `Wacom.PrecisionMode` | Precision Mode: press to turn it on, press again to turn it off |

Everything else an ExpressKey does (keystrokes, modifiers, opening apps, scrolling) the host's own
commands already cover, so this plugin leaves it alone.

macOS only (tested with Wacom Tablet 6.4.14 and a Cintiq Pro 27). On Windows and Linux the keys
show "Wacom not ready".

Made by [**web-engineer**](https://www.web-engineer.co.uk/)
([@web-engineer](https://github.com/web-engineer)).

This is an independent project and is not affiliated with or endorsed by
Wacom. Wacom, Cintiq and ExpressKey are trademarks of Wacom Co., Ltd.

## Which tablet

The keys act on the connected tablet with a pen. With more than one tablet connected, choose
which in the plugin's settings. Wacom's driver also remembers tablets that aren't connected, and
the plugin ignores those.

## Build

```sh
dotnet build -c Release            # installs into ~/.config/LoupixDeck/plugins/wacom
```

Quit LoupixDeck before building; the host keeps the plugin DLL open. The SDK package is restored
from a local LoupixDeck checkout next to this one (see `nuget.config`).

## Licence and credits

MIT licensed: see [LICENSE](LICENSE). Copyright (c) 2026 Web-Engine Limited,
trading as [web-engineer](https://www.web-engineer.co.uk/).

Built on [LoupixDeck](https://github.com/RadiatorTwo/LoupixDeck) and its
[Plugin SDK](https://github.com/RadiatorTwo/LoupixDeck.PluginSdk) by RadiatorTwo,
both MIT licensed. The SDK is not redistributed with this plugin; LoupixDeck
supplies it at runtime.

Need help with a LoupixDeck plugin, a creative-tools integration or something else?
[Get in touch with web-engineer](https://www.web-engineer.co.uk/).
