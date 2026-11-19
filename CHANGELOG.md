# Changelog

## 0.4.1

- Floating prices over items outside are easier to read: they sit above the bin icon instead of on it, use a dark backing instead of a thick outline, and stay within their own tile, so neighbouring prices no longer overlap. The coin icon is gold instead of white.
- Floating prices grow when you zoom the camera out, so they stay readable. Zoomed in, they keep their normal size.

## 0.4.0

- Sell in bulk: put things outside (where the game shows its bin icon), then sell them all from the shop's new "Items outside" panel. The money arrives straight away.
- Things you bought get a floating price over them while they're outside.
- Anything you bought and leave outside is refunded at the start of the day instead of being thrown away for nothing.
- You can't sell the last appliance your menu needs (for example your only sink), whether you're holding it or it's outside. Its label says "Needed".
- All players in an online game need 0.4.0; it can't be mixed with 0.3.x.

## 0.3.2

- Build mode with a mouse: the crane no longer follows your mouse around behind the shop window.
- The click or button press that closes the shop no longer also reaches the game (for example making the build-mode crane pick something up).
- Right-clicking the search box clears it and starts a fresh search.

## 0.3.1

- Your chef no longer walks around behind the shop window when you move the stick or press movement keys (including typing in search).
- The shop now marks its player as "in a menu", the same way the game's own menus do, so other mods that check for this leave that player's input alone.
- While another menu takes over the game's input (for example a second local player's pause menu), the shop window behind it ignores input.

## 0.3.0 (first public release)

- A shop terminal replaces the reroll button at night. Every blueprint the game could offer tonight is listed, in any quantity.
- Category tabs, search (Ctrl+F; on-screen keyboard on controllers; Ctrl+A or Y / Triangle to clear), owned counts and blueprint tooltips.
- Paid items arrive as parcels; a Blueprint button instead drops a normal blueprint (bought as usual) for cabinets and desks.
- Selling: refunds exactly what you paid.
- Mouse, or a controller cursor with aim assist. Local and online co-op.
- Decoration nights every 3rd night by default, with normal items still on sale (configurable; vanilla is every 5th night, decorations only).
- Wallpaper and floor previews on decoration nights.
- `prepare_uninstall` setting to make a run loadable without the mod.
