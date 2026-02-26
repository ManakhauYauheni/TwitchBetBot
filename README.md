<h1><b>Twitch Bet Bot для Dota 2</b></h1>
 <a href="https://dotnet.microsoft.com/ru-ru/download/dotnet/10.0"> <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square" alt=".NET 10.0"/></a> <a href="https://learn.microsoft.com/ru-ru/dotnet/desktop/wpf/"><img src="https://img.shields.io/badge/WPF-Windows-green?style=flat-square&logo=windows&logoColor=white" alt="WPF"/></a> <a href="https://store.steampowered.com/app/570/Dota_2/"> <img src="https://img.shields.io/badge/Game-Dota_2-red?style=flat-square" alt="Dota 2"/></a> <a href="https://github.com/ManakhauYauheni/TwitchBetBot/releases"> <img src="https://img.shields.io/badge/version-1.0.0-blue?style=flat-square" alt="Version 1.0.0"/></a>  <a href="https://www.nuget.org/packages/Dota2GSI"><img src="https://img.shields.io/badge/NuGet-v2.1.1.8897-yellow?style=flat-square&logo=windows&logoColor=white" alt="nuget"/>
 <h2><b>Автоматическое создание и завершение ставок на Twitch по играм Dota2</b></h2> 

О проекте
Twitch Bet Bot — это десктопное приложение для Windows, написанное на C# с использованием WPF. Оно автоматически создаёт и завершает предсказания (ставки) на вашем канале Twitch, анализируя события игры Dota 2 в реальном времени через Game State Integration (GSI).

Приложение построено на паттерне MVVM, что обеспечивает чистое разделение логики и интерфейса. Токен доступа Twitch надёжно шифруется с помощью встроенного механизма Windows DPAPI.

<h2><b>Установка</b></h2>
<details> <summary><b>Вариант 1: Готовая сборка</b></summary>
bash
1. Перейдите на страницу релизов
2. Скачайте publish.rar
3. Распакуйте в любую папку
4. Запустите TwitchBetBot.exe
</details><details> <summary><b>Вариант 2: Сборка из исходников</b></summary>
   <pre>
bash
git clone https://github.com/ManakhauYauheni/TwitchBetBot.git
cd TwitchBetBot
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -o ./publish </pre>
</details>
<h2><b>Настройка Twitch</b></h2>
<h3>Регистрация приложения</h3>

1. Перейдите на https://dev.twitch.tv/console<br>

2. Нажмите "Register Your Application"<br>

3. Заполните форму:<br>
   • Name: Twitch Bet Bot<br>
   • OAuth Redirect URLs: http://localhost:3000, http://localhost<br>
   • Category: Application Integration<br>
4. Скопируйте полученный Client ID<br>
<h3><b>Получение токена</b></h3>

1.Для получения токена вставьте следующую ссылку в строку браузера: <br>
<pre >
https://id.twitch.tv/oauth2/authorize?client_id=YOUR_CLIENT_ID&redirect_uri=http://localhost:3000&response_type=token&scope=channel:read:predictions+channel:manage:predictions
</pre><br>

2.Замените YOUR_CLIENT_ID на свой Client ID, полученный при регистрации

3.Перейдите по ссылке

4.Нажмите "Разрешить"

5.Скопируйте токен из адресной строки (после access_token=)

<h2><b>Настройка Dota 2 GSI</b></h2><br>
<h3><b>Путь к конфигурационным файлам<br></b></h3>
<pre>\Steam\steamapps\common\dota 2 beta\game\dota\cfg\gamestate_integration\</pre><br>

<h3><b>"TwitchBetBot GSI Configuration"</b></h3>
<pre>
"TwitchBetBot Integration Configuration"
{
    "uri"          "http://localhost:3000/"
    "timeout"      "5.0"
    "buffer"       "0.1"
    "throttle"     "0.1"
    "heartbeat"    "10.0"
    "data"
    {
        "auth"            "1"
        "provider"        "1"
        "map"             "1"
        "player"          "1"
        "hero"            "1"
        "abilities"       "1"
        "items"           "1"
        "events"          "1"
        "buildings"       "1"
        "league"          "1"
        "draft"           "1"
        "wearables"       "1"
        "minimap"         "1"
        "roshan"          "1"
        "couriers"        "1"
        "neutralitems"    "1"
    }
}
</pre><br>

<h2><b>Использование</b></h2>
<h3>Первый запуск</h3>
1.Вставьте токен<br>
2.Вставьте Client ID<br>
3.Заполните имя канала<br>
4.Сохраните Конфиг по кнопке<br>
5.Нажмите "Подключиться"<br>
6.После этого можно запускать игру<br>
<h2><b>Благодарности</b></h2>
<div align="center"> <p>Отдельная благодарность <b>antonpup</b> за библиотеку</p> <p> <a href="https://github.com/antonpup/Dota2GSI"> <img src="https://img.shields.io/badge/GitHub-Dota2GSI-181717?style=for-the-badge&logo=github" alt="Dota2GSI"/> </a> </p> <p>Без этой библиотеки проект был бы невозможен</p> </div>
<h2><b>Контакты</b></h2>
<div align="center"> <table> <tr> <td align="center"> <b>GitHub</b><br/> <a href="https://github.com/ManakhauYauheni">github.com/ManakhauYauheni</a> </td>  <td align="center"> <b>Email</b><br/> <a href="mailto:your.email@example.com">manakhov.00@mail.ru</a> </td> </tr> </table> </div>
<p align="center"> <sub>© 2026 TwitchBetBot</sub> </p>
