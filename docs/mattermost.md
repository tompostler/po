# Mattermost

po is moving from Discord to a self-hosted Mattermost server that runs in a container next to po's.
During the move, one po instance serves both platforms, and Discord is removed once Mattermost covers everything.

## What goes to Mattermost today

- Notifications go to every channel in `Notifications:Channels`, so a `mattermost:<channel-id>` entry there gets the restart notice, sync reports, random messages and the "Removed …" startup notice.
- Scheduled images and scheduled messages still go only to Discord, because the database stores their channel as a Discord id.
- Slash commands are still Discord-only.

## Configuration

| Setting | Example | Notes |
|---|---|---|
| `Mattermost__ApiBaseUri` | `http://mattermost:8065` | The server root, without `/api/v4`. |
| `Mattermost__BotToken` | | The `po` bot account's access token. |
| `Notifications__Channels__N` | `mattermost:abc123…` | One entry per channel, in `platform:id` form, mixing platforms freely. |

Both settings are required: without them, po fails at startup with an error naming them.

## Networking

po calls the Mattermost API over the docker network rather than the public URL.
That skips the reverse proxy and TLS, keeps the bot token off the public path, and keeps working when public DNS or the certificate has a problem.
Switching to the public URL later is only a config change.

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

## One-time server setup

1. In the System Console, under **Integrations > Bot Accounts**, enable bot account creation.
2. Under **Integrations > Bot Accounts**, add a bot named `po`, and copy its access token into `Mattermost__BotToken`; it is shown only once.
3. Add the bot to the team, and to each channel it should post in, with `/invite @po`.
4. Copy each channel's id from **View Info** in the channel menu into a `mattermost:<id>` entry in `Notifications:Channels`.
