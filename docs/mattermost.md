# Mattermost

po runs against a self-hosted Mattermost server, in a container next to po's.
It used to run on Discord; Discord was removed in one self-contained commit (v0.15), after a period of serving both platforms from one instance, so reverting that commit is most of the way back.
A revert also has to undo that commit's `RemoveDiscord` migration first, with `dotnet ef database update PlatformAwareChannels`, because po only applies migrations forward; its `Down` restores `SlashCommands` with unregistered rows, so the Discord code registers its commands again on startup.
The removal left the `Platform = 'discord'` rows in place, and kept `IChatPlatform` and the `Platform` column, so another platform stays cheap to add.

## What goes to Mattermost

- Notifications go to every channel in `Notifications:Channels`: the restart notice, sync reports and random messages.
- Scheduled images and scheduled messages go to the channel they were scheduled from.
- Every slash command; see [commands.md](commands.md).
  Discord's `/echo` was dropped rather than ported, because Mattermost's built-in `/echo` would shadow it.
- The nuke commands skip Mattermost's own system posts, such as join notices, and count posts from bot accounts and incoming webhooks as bot messages.

## Configuration

| Setting | Example | Notes |
|---|---|---|
| `Mattermost__ApiBaseUri` | `http://mattermost:8065` | The server root, without `/api/v4`. |
| `Mattermost__BotToken` | | The `po` bot account's access token. |
| `Mattermost__TeamName` | `friends` | The team that slash commands are registered in, by its URL name. |
| `Mattermost__CommandCallbackUri` | `http://po:8081/mattermost/command` | Where Mattermost sends slash commands, on the internal port. |
| `Notifications__Channels__N` | `mattermost:abc123…` | One entry per channel, in `platform:id` form, mixing platforms freely. |

`ApiBaseUri` and `BotToken` are required: without them, po fails at startup with an error naming them.
`TeamName` and `CommandCallbackUri` are read only by slash command registration, which logs an error and registers nothing without them.

## Networking

po calls the Mattermost API over the docker network rather than the public URL.
That skips the reverse proxy and TLS, keeps the bot token off the public path, and keeps working when public DNS or the certificate has a problem.
Switching to the public URL later is only a config change.

Mattermost calls po back on a second port, which only the docker network can reach.
`appsettings.json` declares both ports under `Kestrel:Endpoints`, because declaring any endpoint there replaces the container's default `ASPNETCORE_HTTP_PORTS`: `Public` on 8080 is what Cloudflare reaches, and `Mattermost` on 8081 must never be published or tunnelled.
The command endpoint answers 404 unless the connection arrived on the `Mattermost` port, which it checks directly, because the `Host` header that `[Host]` and `RequireHost` match is set by the client.
The command token is still checked on that port, because any team member can create a slash command that points at it.
Mattermost blocks calls to private addresses, so the `po` host has to be listed in `ServiceSettings.AllowedUntrustedInternalConnections`.
po doesn't redirect HTTP to HTTPS itself, because that would also redirect these internal plain-HTTP calls; public traffic is upgraded at Cloudflare with **Always Use HTTPS** instead.

Image links in attachments have to stay public, because each viewer's client loads `image_url` itself, so `Storage:BaseUri` stays `https://po.tcp.wtf`.
The Mattermost server also fetches the image to work out its size, so the Mattermost container needs to reach `po.tcp.wtf` too.
If that name resolves to a private address from inside the container, Mattermost blocks it until the host is listed in `ServiceSettings.AllowedUntrustedInternalConnections`.
The inline preview is an `img` tag, but the full-size viewer in the web and desktop apps downloads the image with script, so blob downloads send `Access-Control-Allow-Origin: *`; without it, the viewer spins forever.
The header goes on every download, not only requests with an `Origin`, because the viewer reuses the copy the preview cached without one, and Cloudflare lets clients cache images for 4 hours.
That's safe because the signed URL is already the credential, and only the download action sends it, so the API-key actions stay off-limits to browsers.

## Readiness

The two containers start together, so po retries `users/me` with backoff, up to a minute apart, until Mattermost answers, and sends wait until then.
A 401 or 403 means the token is wrong, which retrying won't fix, so po logs an error and every Mattermost send fails immediately instead of waiting forever.
Notifications fan out to channels one after another, so while Mattermost is still starting, channels listed after a Mattermost channel wait for it too.

## Archived and deleted channels

Once at startup, po checks every Mattermost channel it has rows for, and removes the enablement rows and scheduled items of any that were archived or permanently deleted, posting a "Removed …" summary to the notification channels.
Mattermost usually archives rather than deletes, so archived counts as gone, while a channel the bot can't see (403) is kept, because that's a membership problem.
The scheduled items matter most, because the scheduled-image service always takes the earliest row: one for a dead channel fails every minute and holds up every image scheduled after it, until po restarts and this cleanup removes it.

## Rendering

A `ChatMessage` becomes one message attachment in `props.attachments`, a card with a colored edge, a title, text and an optional image.
The API requires `message` even on attachment-only posts, so po sends an empty one.
An attachment's `fallback` is required and is what notifications show, so po fills it from the title, else the text, else the author.
Text longer than 16383 characters, the server's default `MaxPostSize`, is uploaded and posted as a file instead.

## Slash command registration

At startup, po makes the team's custom slash commands match its command definitions: it creates missing ones, updates changed ones, and deletes ones it no longer has.
It only touches commands the bot created, so a command someone made by hand is left alone, even one with a trigger po wants; po logs a warning and doesn't answer that trigger.
Built-in and plugin commands count as taken too, because Mattermost runs them ahead of custom commands, which is why Discord's `/echo` was never ported.
Each command's token comes from the same pass and is kept only in memory, so until a reconcile succeeds, the command endpoint rejects every request.
There's no periodic reconcile, so if a command is deleted or edited in Mattermost's settings, restarting po puts it back.

## Slash command replies

A command's in-channel replies are ordinary posts by the bot, so there's no limit on how many it makes, and `/po reset` can post once per category.

Ephemeral replies, which only the person who ran the command sees, can't use `posts/ephemeral`, because its `create_post_ephemeral` permission is granted only to system admins, and no edition lets it be granted otherwise.
Instead, the endpoint checks the token and parses the arguments before running anything, and returns a bad token or a parse error as an ephemeral HTTP response, which needs no permission.
Otherwise it answers with an empty 200 right away and runs the command in the background, so it never runs into Mattermost's roughly 3-second limit on the first response.
The running command's ephemeral replies go to the request's `response_url`, which Mattermost accepts 5 times within 30 minutes.
That limit only matters for ephemeral replies, which today are only `/schedule-message`'s validation errors and confirmation, one per run.
`response_url` is built from Mattermost's public site URL, so po keeps only its path and sends it to `ApiBaseUri`, like every other call.

## One-time server setup

1. In the System Console, under **Integrations > Bot Accounts**, enable bot account creation.
2. Under **Integrations > Bot Accounts**, add a bot named `po`, and copy its access token into `Mattermost__BotToken`; it is shown only once.
3. Add the bot to the team, and to each channel it should post in, with `/invite @po`.
4. Copy each channel's id from **View Info** in the channel menu into a `mattermost:<id>` entry in `Notifications:Channels`.
5. Under **Integrations > Integration Management**, enable custom slash commands.
6. Set `Mattermost__TeamName` to the team's URL name, and `Mattermost__CommandCallbackUri` to po's internal-port endpoint.
7. Make the bot a team admin, under **System Console > User Management > Teams**, in the team's member list.
   Registering slash commands needs `manage_own_slash_commands` in the team, which ordinary members didn't have on this server, so without it registration fails with a 403 on `GET /api/v4/commands`.
   Team admin is enough; the bot doesn't need to be a system admin, because ephemeral replies avoid `posts/ephemeral`.
