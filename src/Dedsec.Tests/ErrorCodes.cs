using System;
using System.ComponentModel;
using System.Globalization;
namespace Dedsec.Tests;
public static class ErrorCodes
{
    public static string Explain(string input,bool stopCode)
    {
        input=input.Trim();uint code;
        if(input.StartsWith("0x",StringComparison.OrdinalIgnoreCase)){if(!uint.TryParse(input[2..],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out code))return "Введите код вида 0x80070005 или десятичное число.";}
        else if(!uint.TryParse(input,out code))return "Код не распознан. Сохраните источник ошибки и её полный текст.";
        if(stopCode)return code switch
        {
            0x124=>"WHEA_UNCORRECTABLE_ERROR: Windows получила неисправленную аппаратную ошибку. Источник (CPU, память, PCIe и др.) определяется по записи WHEA и дампу. Проверьте штатные частоты, охлаждение и питание; один STOP-код не устанавливает неисправный компонент.",
            0x116=>"VIDEO_TDR_FAILURE: видеоподсистема не восстановилась после таймаута. Возможны драйвер, перегрев, питание или нестабильность GPU. Проверьте соседние события и дамп; это не однозначный диагноз видеокарты.",
            0x1A=>"MEMORY_MANAGEMENT: ошибка управления памятью. Возможны RAM, драйвер, накопитель/файл подкачки или повреждение данных. Нужны параметры BSOD и дамп; проверка небольшой части RAM не исключает дефект.",
            0xEF=>"CRITICAL_PROCESS_DIED: завершился критический процесс. Начните с анализа дампа, событий диска и системных файлов. Причина не определяется одним названием STOP.",
            0x9F=>"DRIVER_POWER_STATE_FAILURE: проблема перехода устройства/драйвера между состояниями питания. Сопоставьте с выходом из сна, USB и недавним обновлением драйверов; для точности нужен дамп.",
            0x133=>"DPC_WATCHDOG_VIOLATION: превышен допустимый период DPC/прерываний. Причину ищут в драйверах и трассировке/дампе; часто нужна проверка драйверов контроллера и устройств, но угадывать виновника по STOP нельзя.",
            _=>$"STOP 0x{code:X8}: в локальном справочнике нет специального объяснения. Используйте Microsoft Bug Check Code Reference и анализ дампа в WinDbg. Не трактуйте этот номер как Win32/HRESULT."
        };
        uint win32=(code&0xffff0000)==0x80070000?code&0xffff:code;
        bool hresult=(code&0x80000000)!=0&&win32==code;
        string message=hresult?(System.Runtime.InteropServices.Marshal.GetExceptionForHR(unchecked((int)code))?.Message??"Описание HRESULT недоступно"):new Win32Exception(unchecked((int)win32)).Message;
        string next=win32 switch {5=>"Проверьте права на конкретный объект. Не выдавайте полный доступ всем пользователям; при необходимости запустите нужный инструмент администратором.",2 or 3=>"Проверьте существование нужного файла/папки и настройки программы. Не скачивайте системные DLL со сторонних сайтов.",32=>"Файл занят другим процессом. Сохраните работу и закройте использующее его приложение.",112=>"Освободите место на целевом томе, проверив категории удаления.",87=>"Проверьте синтаксис и параметры команды. Код сам по себе не указывает на поломку оборудования.",_=>"Сопоставьте код с источником и полным сообщением. Некоторые коды относятся к собственному пространству приложения, а не Win32."};
        return $"Код 0x{code:X8}; системный текст {(hresult?"HRESULT":"Win32")}{(win32!=code?$" ({win32})":"")}: {message}\n\n{next}";
    }
}
