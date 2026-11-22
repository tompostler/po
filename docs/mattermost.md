# Mattermost

po is moving from Discord to a self-hosted Mattermost server that runs in a container next to po's.
During the move, one po instance serves both platforms, and Discord is removed once Mattermost covers everything.

## What goes to Mattermost today

- Notifications go to every channel in `Notifications:Channels`, so a `mattermost:<channel-id>` entry there gets the restart notice, sync reports, random messages and the "Removed …" startup notice.
- Scheduled images and scheduled messages go to whichever platform their row names, but only the Discord commands create them so far.
- Slash commands are being ported one at a time; see [commands.md](commands.md). So far `/commandlist` runs on Mattermost, and the rest are still Discord-only.

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

## Readiness

The two containers start together, so po retries `users/me` with backoff, up to a minute apart, until Mattermost answers, and sends wait until then.
A 401 or 403 means the token is wrong, which retrying won't fix, so po logs an error and every Mattermost send fails immediately instead of waiting forever.
Notifications fan out to channels one after another, so while Mattermost is still starting, channels listed after a Mattermost channel wait for it too.

## Rendering

A `ChatMessage` becomes one message attachment in `props.attachments`, Mattermost's equivalent of a Discord embed.
The API requires `message` even on attachment-only posts, so po sends an empty one.
An attachment's `fallback` is required and is what notifications show, so po fills it from the title, else the text, else the author.
Text longer than 16383 characters, the server's default `MaxPostSize`, is uploaded and posted as a file instead.

## Slash command registration

At startup, po makes the team's custom slash commands match its command definitions: it creates missing ones, updates changed ones, and deletes ones it no longer has.
It only touches commands the bot created, so a command someone made by hand is left alone, even one with a trigger po wants; po logs a warning and doesn't answer that trigger.
Built-in and plugin commands count as taken too, because Mattermost runs them ahead of custom commands, which is why po's Discord-only `/echo` was never ported.
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
