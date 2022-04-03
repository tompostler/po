Write-Host -ForegroundColor Cyan 'Logging in to subscription....';
$subscriptionId = 'b12eec98-4290-4ba5-ae5e-c01d4c83db72';
$context = Get-AzContext;
if ($null -eq $context) {
    Login-AzAccount -SubscriptionId $subscriptionId;
}
elseif ($context.Subscription.Id -ne $subscriptionId) {
    Select-AzSubscription -SubscriptionId $subscription -ErrorAction Stop;
}
Write-Host;

# Note: the following needs to be kept up-to-date with any necessary config changes
Write-Host -ForegroundColor Cyan 'Generating local settings....';
$localSettings = [PSCustomObject]@{
    Discord = [PSCustomObject]@{
        BotPrimaryGuildId        = (Get-AzKeyVaultSecret -VaultName tompostler -Name discord-pobot-bot-primary-guild-id -AsPlainText);
        BotNotificationChannelId = (Get-AzKeyVaultSecret -VaultName tompostler -Name discord-pobot-bot-notification-channel-id -AsPlainText);
        BotToken                 = (Get-AzKeyVaultSecret -VaultName tompostler -Name discord-pobot-bot-token -AsPlainText);
    };
    Sql     = [PSCustomObject]@{
        ConnectionString = (
            'Server=tcp:tompostler.database.windows.net,1433;Initial Catalog=polocal;Persist Security Info=False;' `
                + 'User ID=sqladmin;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Password=' `
                + (Get-AzKeyVaultSecret -VaultName tompostler -Name tompostler-sqladmin-password -AsPlainText) `
                + ';');
    };
};
$localSettingsPath = Join-Path ($PSScriptRoot) '.\src\po\appsettings.Development.json';
# Create the item (including path!) if it doesn't exist
New-Item -Path $localSettingsPath -ItemType File -Force | Out-Null;
$localSettings | ConvertTo-Json | Set-Content -Path $localSettingsPath;
Write-Host

Write-Host -ForegroundColor Green 'Done!'
