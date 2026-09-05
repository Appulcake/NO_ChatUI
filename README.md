## NO Chat UI (I'm not good with mod names)

Separate general chat (player and mission messages are here) and kill feed, configurable in `BepInEx/config/NO_ChatUI.cfg` or via BepInEx Configuration Manager's F1 menu.
Independently position and resize general chat and kill feed:
- Set corner anchor
- Set X Y offset (counted from the anchor inwards)
- Set scale of panel
- Set width - this sets a fixed width the panel will be at all times, set this to 0 for auto width (width will resize based on longest message)
- Set max width (when width is in auto mode, this is the max width it'll allow the panel to become)

When combining kill feed into general messages (so it's like vanilla), you can still move/resize that panel with the general chat settings (kill feed's other settings are ignored).

Message timing settings allow you to control how long each message and kill feed entry shows before disappearing (default configs are same as vanilla defaults for these).

History allows you to see history for both panels, they show when you mouse over the panel (or start typing in chat, though chat disables rewired controls so scrolling while typing won't work).
When mouse overing a panel, you can scroll the history with either `Zoom View` or `Field of View` vanilla binds (so by default scrollwheel and I believe page up/down).
History size allows you to set how many lines max the histories save.
When kill feed is combined into general chat, they have a combined chronological history of both their elements.

By default kill feed is split and put to top right corner, with some Y offset to be below vanilla elements:
<img width="1280" alt="NuclearOption_THj1etWFW8" src="https://github.com/user-attachments/assets/5a11bb1f-7ce9-43f4-9446-4fe0aa390f46" />

https://github.com/user-attachments/assets/5fbbe4f9-3832-4a1b-9333-bae578a49acd

