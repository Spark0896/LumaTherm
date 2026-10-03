# LumaTherm 1.2.0 — Arctic redesign / Редизайн Arctic

## Русский

Новый интерфейс Arctic, удобный редактор цветов и исправления повседневных функций.
Редизайн включён в установщик и portable-архив этой версии.

- Главная страница с температурой GPU, историей за 60 секунд и состоянием устройства.
- Редактор профиля: перетаскивание точек, добавление и удаление, HEX, RGB и готовые цвета.
- Профиль по умолчанию: **35°C — синий `#006BFF`, 65°C — зелёный `#3CFF00`, 85°C — красный `#FF0000`**.
- Градиент в интерфейсе использует тот же расчёт, что и подсветка. Исправлены переходы через чёрный и серый.
- Исправлены меню трея, сворачивание, восстановление окна и завершение приложения.
- Переключатель автозапуска применяется сразу и показывает состояние Windows. Автозапуск открывает приложение в фоне.
- Проверка числовых полей предотвращает сохранение неверных значений. Применение и отмена теста подсветки корректно завершают временный режим.
- Русский и английский интерфейс, адаптация к размеру окна и читаемые элементы управления.

**Обновление с 1.1.0:** закройте LumaTherm через «Выход» в трее и запустите новый установщик. Настройки и пользовательский профиль сохраняются. Для возврата к новой палитре нажмите «Цвета по умолчанию».

Скачайте `LumaTherm-1.2.0-win-x64-setup.exe` или `LumaTherm-1.2.0-portable-win-x64.zip` и проверьте SHA-256 по `SHA256SUMS.txt`. Windows 11 x64 22H2+, драйвер NVIDIA и устройство Windows Dynamic Lighting / LampArray обязательны; отдельная установка .NET не нужна. Для регистрации подсветки установщик просит разрешение на импорт комплектного публичного сертификата. Закрытый ключ в релиз не входит.

![Главная страница на русском](https://raw.githubusercontent.com/Spark0896/LumaTherm/v1.2.0/docs/screenshots/ru/dashboard.png)
![Настройки на русском](https://raw.githubusercontent.com/Spark0896/LumaTherm/v1.2.0/docs/screenshots/ru/settings.png)

[Установка](https://github.com/Spark0896/LumaTherm/blob/v1.2.0/docs/installation.ru.md) · [Portable](https://github.com/Spark0896/LumaTherm/blob/v1.2.0/docs/portable.ru.md) · [Все снимки](https://github.com/Spark0896/LumaTherm/blob/v1.2.0/README.ru.md)

## English

A refreshed Arctic interface, a clearer color editor, and fixes to everyday workflows.
Both the setup and portable archive include the redesign.

- Dashboard with live GPU temperature, 60-second history, and device status.
- Profile editor with draggable stops, add/remove actions, HEX, RGB, and color swatches.
- Default profile: **35°C — blue `#006BFF`, 65°C — green `#3CFF00`, 85°C — red `#FF0000`**.
- Interface gradients use the same mapping as physical lighting. Black and gray transitions preserve the neighboring hue.
- Fixed tray menus, minimize, restore, and application exit.
- Windows startup changes apply immediately and reflect Windows state. Startup opens the app in the background.
- Numeric validation prevents invalid saves. Apply and Cancel correctly finish temporary lighting tests.
- English and Russian UI, responsive window layouts, and readable controls.

**Upgrading from 1.1.0:** exit LumaTherm from its tray menu and run the new setup. Saved preferences and custom profiles are preserved. Use **Default colors** to restore the new factory palette.

Download `LumaTherm-1.2.0-win-x64-setup.exe` or `LumaTherm-1.2.0-portable-win-x64.zip` and verify its SHA-256 against `SHA256SUMS.txt`. Requires Windows 11 x64 22H2+, an NVIDIA driver, and a Windows Dynamic Lighting / LampArray device; no separate .NET installation is needed. Setup requests consent to import its bundled public certificate for lighting registration. The release contains no private signing key.

![Dashboard in English](https://raw.githubusercontent.com/Spark0896/LumaTherm/v1.2.0/docs/screenshots/en/dashboard.png)
![Settings in English](https://raw.githubusercontent.com/Spark0896/LumaTherm/v1.2.0/docs/screenshots/en/settings.png)

[Installation](https://github.com/Spark0896/LumaTherm/blob/v1.2.0/docs/installation.md) · [Portable](https://github.com/Spark0896/LumaTherm/blob/v1.2.0/docs/portable.md) · [All screenshots](https://github.com/Spark0896/LumaTherm/blob/v1.2.0/README.md)

## Validation / Проверка

Release validation covers automated tests, signed artifacts, and an in-place upgrade from 1.1.0 on the maintainer's Windows computer. An actual Windows reboot, sleep/resume, and visual LED inspection are separate hardware acceptance steps; they are not claimed by this release.
