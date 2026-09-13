# LumaTherm 1.1.0

## English

LumaTherm 1.1.0 is the first public release of the free, open-source Windows
desktop application that maps compatible Dynamic Lighting devices to GPU
temperature.

### Highlights

- Smooth, saturated blue → magenta → pure-red thermal gradient with editable
  temperature stops and eased transitions near every physical-LED stop.
- Interactive lighting test window: preview a temperature and colors without a
  game or benchmark.
- NVIDIA NVML sensor first, with MSI Afterburner shared memory as an optional,
  read-only fallback.
- Background tray operation, configurable tray menu, optional notifications,
  and opt-in Windows startup that resumes the last thermal-mode state.
- English, Russian, and system-language interface options.
- No GCC, vendor DLL, or MSI Afterburner installation is required on a clean
  supported Windows 11 system.

### Downloads

- `LumaTherm-1.1.0-win-x64-setup.exe` — recommended installer. It supports a
  custom destination plus optional Desktop and Start Menu shortcuts.
- `LumaTherm-1.1.0-portable-win-x64.zip` — self-contained portable deployment.
  Register it explicitly from a permanent folder using its included
  `install.ps1`.
- `SHA256SUMS.txt` — SHA-256 values for both downloads.

The installer/portable registration uses a signed sparse Windows package so
that LumaTherm can participate in Dynamic Lighting. This locally signed release
imports its public certificate into the Windows machine trusted-root store only
after explicit consent; no private key is installed. The installer removes the
exact certificate when LumaTherm is uninstalled.

For a LampArray device shared with another controller, enable Windows Dynamic
Lighting and prioritize LumaTherm in the Windows background controller list.
LumaTherm does not modify GIGABYTE Control Center or other vendor software.

## Русский

LumaTherm 1.1.0 — первый публичный релиз бесплатного open-source приложения
для Windows, которое связывает температуру GPU с цветом совместимой подсветки
Dynamic Lighting.

### Главное

- Плавный насыщенный градиент синий → пурпурный → чистый красный с
  редактируемыми температурными точками и мягким входом в каждую точку на
  реальных светодиодах.
- Интерактивный тест подсветки: предварительный просмотр температуры и цветов
  без игры или бенчмарка.
- Основной датчик NVIDIA NVML и необязательный резервный датчик MSI Afterburner
  только для чтения общей памяти.
- Фоновая работа через трей, настраиваемое меню трея, необязательные
  уведомления и автозапуск по выбору пользователя с восстановлением последнего
  состояния терморежима.
- Интерфейс на русском, английском или системном языке.
- На чистой поддерживаемой Windows 11 не требуются GCC, vendor DLL или
  установленный MSI Afterburner.

### Файлы релиза

- `LumaTherm-1.1.0-win-x64-setup.exe` — рекомендуемый установщик с выбором
  папки и необязательными ярлыками на Рабочем столе и в меню «Пуск».
- `LumaTherm-1.1.0-portable-win-x64.zip` — самодостаточная portable-версия.
  Распакуйте её в постоянную папку и явно зарегистрируйте через приложенный
  `install.ps1`.
- `SHA256SUMS.txt` — SHA-256 для обоих файлов.

Для участия в Dynamic Lighting установщик/portable-регистрация использует
подписанный разреженный пакет Windows. Эта локально подписанная сборка
импортирует публичный сертификат в доверенное корневое хранилище компьютера
только после явного согласия; закрытый ключ не устанавливается. При удалении
LumaTherm установщик удаляет этот точный сертификат.

Если устройством LampArray одновременно управляет другой контроллер, включите
Windows Dynamic Lighting и поставьте LumaTherm выше него в списке фоновых
контроллеров Windows. LumaTherm не изменяет GIGABYTE Control Center и другое
ПО производителей.

## Screenshots / Скриншоты

### Dashboard / Главный экран

![LumaTherm dashboard](https://raw.githubusercontent.com/Spark0896/LumaTherm/main/docs/screenshots/dashboard.png)

### Settings / Настройки

![LumaTherm settings](https://raw.githubusercontent.com/Spark0896/LumaTherm/main/docs/screenshots/settings.png)

### Interactive lighting test / Интерактивный тест подсветки

![LumaTherm lighting test](https://raw.githubusercontent.com/Spark0896/LumaTherm/main/docs/screenshots/lighting-test.png)
