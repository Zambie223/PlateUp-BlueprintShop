# Blueprint Shop

A PlateUp! mod that replaces the random nightly blueprint letters with a shop. Every blueprint the game could offer you tonight is listed at once, and you can buy as many of each as you can afford.

The goal is to remove the luck of which blueprints show up. Prices, discounts, what's eligible for your restaurant, and when upgrades unlock all follow the base game's rules. The one deliberate change is the decoration schedule: by default, decorations come more often and normal items stay on sale on those nights. Two settings switch that back to vanilla (see [Changes to the base game's rules](#changes-to-the-base-games-rules)).

## Installing

1. Subscribe to the **Harmony** mod on the Steam Workshop (id `2898033283`) and launch PlateUp once so it downloads.
2. Download `BlueprintShop-<version>.zip` from this repo's [Releases](../../releases) page.
3. Extract it into PlateUp's `Mods` folder, so you end up with `Mods\BlueprintShop\BlueprintShop.dll`.
   The `Mods` folder sits next to `PlateUp.exe`, by default `C:\Program Files (x86)\Steam\steamapps\common\PlateUp\PlateUp\Mods`. Create it if it doesn't exist.
4. Start the game.

**Online play: every player needs this mod, the same version.** Players without it can't display the shop and may run into errors.

To update, replace the files in `Mods\BlueprintShop\`. To remove the mod, see [Saves and uninstalling](#saves-and-uninstalling) first.

## How it works

- **Opening the shop:** at night, the reroll button outside your front door becomes a **Shop** terminal. Press interact on it to open the menu. On the very first night (day 0), the terminal appears as soon as you have money, for example in Turbo mode.
- **Browsing:** items are grouped by what they do (Cooking, Automation, Plumbing, Front of House and so on). Upgrades sit in the same tab as the appliance they upgrade, and "Staple", "Upgrade" and "One only" show as badges on the card. Each card also shows how many you already own. Hover over an item to see the same details as its blueprint tooltip.
- **Searching:** type in the search box above the tabs (click it, or press Ctrl+F). On a controller, selecting the search box opens an on-screen keyboard.
- **Buying:** add items to your cart and press **Purchase**. Paid items arrive as parcels on the letter tiles by the door, like your starting appliances. The top right shows your balance and what you'll have left after the cart. Your cart is remembered if you close the menu, until the day starts.
- **Blueprints:** each item also has a **Blueprint** button. It drops a normal, unpaid blueprint outside, for a blueprint cabinet or a research, copying or discount desk.
- **Selling:** pick up an appliance (or an unopened parcel), open the shop, and use **Sell** at the top of the cart. You get back exactly what you paid for it: the discounted price if you bought it on discount, and nothing for things you got for free (starting kit, card rewards). The button asks you to click twice, because selling removes the appliance.
- **Decoration nights** (every 3rd night by default): the shop adds every eligible decoration and event item, plus every wallpaper and floor (free). Normal appliances are still on sale.

### Controls

| | Keyboard and mouse | Controller |
|---|---|---|
| Point | Mouse | Left stick moves a cursor, which snaps to the nearest button |
| Select | Left click | Interact, Grab or Menu Select |
| Remove one from cart | Right-click a card | Use the "-" button in the cart |
| Search | Click the search box or Ctrl+F, then type | Select the search box for the on-screen keyboard |
| Clear search | Ctrl+A, or the Clear button | Y / Triangle (your "third action" button if you've rebound it), or the Clear button |
| Scroll | Mouse wheel | Hold the cursor near the top or bottom of the list |
| Close | Esc (first Esc leaves the search box) | Back or Start (first press closes the keyboard) |

Your chef stands still while the menu is open. Other players keep playing. In local co-op, each player who opens the shop gets their own half or quarter of the screen.

## Rules it keeps from the base game

- **What's offered:** the game's own shop pool. Items that need a specific dish, theme or appliance, "only one allowed" items you already own, and staples all work as in vanilla.
- **Upgraded appliances:** available on any night the base game could offer them, which is night 5 onward (night 6 with the vanilla decoration schedule, since night 5 is then decorations only), or earlier if a card raises the upgrade chance.
- **Prices:** exactly the blueprint price: the item's price tier, times every active discount. That includes discount cards, restaurant settings such as Turbo, and the Lunar New Year event. With the "random prices" card, each item gets one random price per night instead of one per letter.
- **"Only one allowed" items:** limited to one per order, and blocked while one is still waiting in a parcel or as a blueprint.
- **Free bonus blueprints from cards** still arrive as free random letters, as in vanilla. They're a free bonus, not a purchase.
- **Rerolling:** gone, since there is nothing left to reroll.

## Changes to the base game's rules

Two settings in `BlueprintShop.cfg` (see [Troubleshooting](#troubleshooting) for where it is) change when things are on sale. In online games, the host's settings apply.

| Setting | Default | Vanilla | What it does |
|---|---|---|---|
| `decor_interval` | `3` | `5` | Decorations, wallpapers and floors are in the shop every Nth night. |
| `normal_items_on_decor_nights` | `true` | `false` | Decoration nights also sell normal appliances. In vanilla those nights only have decorations. |

With the defaults, normal appliances are on sale every night, so upgrades can appear from night 5 instead of night 6. Restart the game after changing a setting.

## Saves and uninstalling

The shop itself is never saved. The one thing the mod adds to save files is a small "price paid" record on each appliance you buy, so selling can refund the right amount on later nights.

Because of that record, **a run that was saved with the mod can't be loaded without it.** Your franchise, other runs, and new runs are unaffected. To finish a run without the mod:

1. Set `prepare_uninstall=true` in `BlueprintShop.cfg` (add the line if it isn't there).
2. Load the run and play into the next night. The game autosaves at the start of each night, and the mod removes all its records before then.
3. Remove the mod. The run now loads without it.

While `prepare_uninstall` is on, purchases aren't recorded and refunds are $0.

## Troubleshooting

Verbose logging is off by default. To turn it on, do either of these:

- Add the launch option `-blueprintshop-verbose` (Steam → PlateUp → Properties → Launch Options).
- Set `verbose=true` in `BlueprintShop.cfg`. The mod creates this file on first run, next to the game's log at `%USERPROFILE%\AppData\LocalLow\It's Happening\PlateUp\`.

If the menu text ever shows up as symbols, add `font=<name>` to `BlueprintShop.cfg`. With verbose logging on, the log lists every font name the game has loaded.

Logs go to `Player.log` in the same folder. Every line from this mod starts with `[Blueprint Shop]`. Please include it when reporting a bug.

## Known limitations

- The terminal reuses the reroll button's model, relabelled "Shop".
- Wallpaper and floor names come from the game's internal asset names, because the game has no display names for them.
- If the letter area by the door is full, an order is refused until you open or move some parcels. Nothing is ever stacked on one tile.

## Building from source

You need:

- The [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer. The mod targets `netstandard2.0`, C# 9.
- PlateUp! installed through Steam, and the Harmony Workshop item subscribed. The project compiles against the game's own DLLs and Harmony's `0Harmony.dll` on your machine; none of them are in this repo, and none are copied into the build output.

Then run:

```
dotnet build -c Release
```

The build copies `BlueprintShop.dll` and `.pdb` into the game's `Mods\BlueprintShop\` folder. Restart the game to load a new build. Add `-p:DeployMod=false` to build without copying. To remove the local copy again: `dotnet msbuild -t:UndeployFromPlateUp`.

If PlateUp isn't in the default Steam library, either set the `PLATEUP_GAME_DIR` environment variable to the folder containing `PlateUp.exe`, or create `mods\Directory.Build.local.props` (git-ignored):

```xml
<Project><PropertyGroup>
  <PlateUpGameDir>D:\SteamLibrary\steamapps\common\PlateUp\PlateUp</PlateUpGameDir>
</PropertyGroup></Project>
```

## License

The source code in this repository is under the [MIT License](LICENSE). PlateUp! and its code and assets belong to their respective owners and aren't included here.

---

### Responsible AI use disclosure

This mod was written with the assistance of an AI coding tool (Claude, by Anthropic), under the direction of the author (Zambie). Its behavior is based on the game's own code, which was read to match the base game's shop rules, rather than on AI recall. It is play-tested in-game, including online co-op, before each release.
