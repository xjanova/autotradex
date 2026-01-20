$exchangeDir = 'D:\Code\APP Thaiprompt\AutoTrade-X\src\AutoTradeX.UI\Assets\Exchanges'
$coinDir = 'D:\Code\APP Thaiprompt\AutoTrade-X\src\AutoTradeX.UI\Assets\Coins'

New-Item -ItemType Directory -Force -Path $exchangeDir | Out-Null
New-Item -ItemType Directory -Force -Path $coinDir | Out-Null

# Download Exchange logos from CoinMarketCap
$client = New-Object System.Net.WebClient

# Exchanges
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/exchanges/64x64/270.png', "$exchangeDir\binance.png")
Write-Host "Downloaded: binance"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/exchanges/64x64/311.png', "$exchangeDir\kucoin.png")
Write-Host "Downloaded: kucoin"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/exchanges/64x64/294.png', "$exchangeDir\okx.png")
Write-Host "Downloaded: okx"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/exchanges/64x64/521.png', "$exchangeDir\bybit.png")
Write-Host "Downloaded: bybit"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/exchanges/64x64/302.png', "$exchangeDir\gateio.png")
Write-Host "Downloaded: gateio"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/exchanges/64x64/436.png', "$exchangeDir\bitkub.png")
Write-Host "Downloaded: bitkub"

# Coins
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/1.png', "$coinDir\btc.png")
Write-Host "Downloaded: btc"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/1027.png', "$coinDir\eth.png")
Write-Host "Downloaded: eth"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/5426.png', "$coinDir\sol.png")
Write-Host "Downloaded: sol"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/825.png', "$coinDir\usdt.png")
Write-Host "Downloaded: usdt"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/1839.png', "$coinDir\bnb.png")
Write-Host "Downloaded: bnb"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/52.png', "$coinDir\xrp.png")
Write-Host "Downloaded: xrp"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/3408.png', "$coinDir\usdc.png")
Write-Host "Downloaded: usdc"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/2010.png', "$coinDir\ada.png")
Write-Host "Downloaded: ada"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/5805.png', "$coinDir\avax.png")
Write-Host "Downloaded: avax"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/74.png', "$coinDir\doge.png")
Write-Host "Downloaded: doge"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/6636.png', "$coinDir\dot.png")
Write-Host "Downloaded: dot"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/1975.png', "$coinDir\link.png")
Write-Host "Downloaded: link"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/3890.png', "$coinDir\matic.png")
Write-Host "Downloaded: matic"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/5994.png', "$coinDir\shib.png")
Write-Host "Downloaded: shib"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/2.png', "$coinDir\ltc.png")
Write-Host "Downloaded: ltc"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/1958.png', "$coinDir\trx.png")
Write-Host "Downloaded: trx"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/7083.png', "$coinDir\uni.png")
Write-Host "Downloaded: uni"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/3794.png', "$coinDir\atom.png")
Write-Host "Downloaded: atom"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/512.png', "$coinDir\xlm.png")
Write-Host "Downloaded: xlm"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/6535.png', "$coinDir\near.png")
Write-Host "Downloaded: near"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/21794.png', "$coinDir\apt.png")
Write-Host "Downloaded: apt"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/11841.png', "$coinDir\arb.png")
Write-Host "Downloaded: arb"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/11840.png', "$coinDir\op.png")
Write-Host "Downloaded: op"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/7278.png', "$coinDir\aave.png")
Write-Host "Downloaded: aave"
$client.DownloadFile('https://s2.coinmarketcap.com/static/img/coins/64x64/11419.png', "$coinDir\ton.png")
Write-Host "Downloaded: ton"

Write-Host "Done!"
