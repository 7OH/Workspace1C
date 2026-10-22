using System;
using System.Collections.Generic;

namespace Workspace1C.Services
{
    /// <summary>
    /// Набор модификаторов горячей клавиши в платформонезависимом виде.
    /// Никаких ссылок на WPF/Avalonia нет: платформенные обработчики
    /// (PreviewKeyDown/KeyDown) конвертируют свои флаги до сравнения.
    /// </summary>
    [Flags]
    public enum HotkeyModifiers
    {
        None = 0,
        Control = 1,
        Shift = 2,
        Alt = 4,
        Windows = 8,
    }

    /// <summary>
    /// Чистый разбор и матчинг настраиваемых горячих клавиш (issue #32:
    /// «Shift+клавиша не работает вошё исключением двух модификаторов»).
    /// Работает со строкой жеста в формате HotkeyBox («Ctrl+Shift+F9», «Shift+0»,
    /// «NumPad+» и т.п.) и фактическим нажатием: именем ФИЗИЧЕСКОЙ клавиши
    /// (Key.ToString() в WPF/Avalonia даёт «F9», «P», «D5», «OemPlus») и набором
    /// модификаторов. Сравнение идёт по имени клавиши, а не по символу раскладки,
    /// поэтому «Shift+клавиша» работает независимо от раскладки и верхнего регистра,
    /// не полагаясь на нюансы платформенного KeyBinding/KeyGesture.
    /// Неверные/старые записи вроде «Shift+П» (символ русской раскладки) не парсятся
    /// и совпадения не дают см. <see cref="TryParse"/>.
    /// </summary>
    public static class HotkeyMatcher
    {
        /// <summary>
        /// Разбирает строку жеста («Ctrl+Shift+F9», «Shift+F5», «Shift+Вставка» —
        /// если имя клавиши валидно) на каноническое имя клавиши и модификаторы.
        /// Поведение повторяет WPF-парсер MainWindow.TryParseKeyGesture: разделитель '+',
        /// синонимы Del/Ins/Esc, «0»→«D0», «+»/«=»→«OemPlus», «NumPad+»→«Add» и т.п.
        /// Возвращает false для пустых/служебных значений («—», «-», «Нет», «None»),
        /// для строк без клавиши (один модификатор) и для имён, не являющихся
        /// именами клавиш (в т.ч. символы раскладки вроде «П» — см. Замечание).
        /// </summary>
        public static bool TryParse(string? text, out string keyName, out HotkeyModifiers modifiers)
        {
            keyName = string.Empty;
            modifiers = HotkeyModifiers.None;
            if (string.IsNullOrWhiteSpace(text) ||
                string.Equals(text.Trim(), "—", StringComparison.Ordinal) ||
                string.Equals(text.Trim(), "-", StringComparison.Ordinal) ||
                string.Equals(text.Trim(), "Нет", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text.Trim(), "None", StringComparison.OrdinalIgnoreCase))
                return false;

            // Разбор вручную, а не Split('+'): клавиша «NumPad+» и сочетания вида
            // «Ctrl++»/«Ctrl+-» содержат '+'/'-' в составе имени клавиши (как их
            // сохраняет HotkeyBox: KeyToDisplay(Key.Add) = «NumPad+»).
            var tokenStart = 0;
            while (tokenStart < text.Length)
            {
                var plus = text.IndexOf('+', tokenStart);
                if (plus < 0)
                {
                    // Последний токен — имя клавиши.
                    var normalized = NormalizeKeyName(text.Substring(tokenStart));
                    if (normalized is null)
                        return false;
                    keyName = normalized;
                    return true;
                }

                var token = text.Substring(tokenStart, plus - tokenStart);
                // «Ctrl++»: между разделителями пусто — клавиша «+» (OemPlus).
                if (token.Length == 0)
                {
                    var plusKey = NormalizeKeyName(text.Substring(plus));
                    if (plusKey is null)
                        return false;
                    keyName = plusKey;
                    return true;
                }

                // «Ctrl+NumPad+»: '+' в конце строки принадлежит имени клавиши
                // цифрового блока (Add), а не является разделителем.
                if (token.Equals("NumPad", StringComparison.OrdinalIgnoreCase) && plus == text.Length - 1)
                {
                    keyName = "Add";
                    return true;
                }

                if (!TryParseModifierToken(token, out var m))
                    return false;
                modifiers |= m;
                tokenStart = plus + 1;
            }

            return false; // остались только разделители («Ctrl+») — жеста нет
        }

        /// <summary>
        /// Разбирает токен модификатора («Ctrl»/«Control», «Shift», «Alt», «Win»/«Windows»/«Meta»).
        /// </summary>
        private static bool TryParseModifierToken(string token, out HotkeyModifiers modifier)
        {
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Control", StringComparison.OrdinalIgnoreCase))
                modifier = HotkeyModifiers.Control;
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                modifier = HotkeyModifiers.Shift;
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                modifier = HotkeyModifiers.Alt;
            else if (token.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                     token.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                     token.Equals("Meta", StringComparison.OrdinalIgnoreCase))
                modifier = HotkeyModifiers.Windows;
            else
            {
                modifier = HotkeyModifiers.None;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Совпадает ли фактическое нажатие (имя физической клавиши + модификаторы)
        /// с сохранённым жестом. Точное равенство модификаторов: Ctrl не совпадает
        /// с Ctrl+Shift, Alt+Shift — с Shift и т.п.
        /// </summary>
        public static bool Matches(string? gesture, string actualKeyName, HotkeyModifiers actualModifiers)
        {
            if (!TryParse(gesture, out var expectedKey, out var expectedModifiers))
                return false;

            var actual = NormalizeKeyName(actualKeyName);
            if (actual is null || string.Equals(actual, "None", StringComparison.OrdinalIgnoreCase))
                return false;

            if (expectedModifiers != actualModifiers)
                return false;

            return string.Equals(expectedKey, actual, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Приводит имя клавиши к каноническому виду (как его дают Key.ToString() в
        /// WPF/Avalonia): «Del»→«Delete», «0»→«D0», «+»→«OemPlus», «NumPad+»→«Add»,
        /// символы вроде «,»/«.»/«?» отображаемых клавиш — к Oem-именам.
        /// Возвращает null для невалидных имён: пустая строка, один из модификаторов,
        /// «None», символы раскладки (кириллица и прочее не-ASCII) и т.п.
        /// </summary>
        public static string? NormalizeKeyName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var t = name.Trim();
            var lower = t.ToLowerInvariant();

            switch (lower)
            {
                case "del":
                    return "Delete";
                case "ins":
                    return "Insert";
                case "esc":
                    return "Escape";
                case "none":
                    return null;
                case "numpad+":
                    return "Add";
                case "numpad-":
                    return "Subtract";
                case "numlock":
                    return "NumLock";
                case "capslock":
                    return "CapsLock";
                case "scrolllock":
                    return "ScrollLock";
                case "printscreen":
                    return "PrintScreen";
                case "snapshot":
                    return "PrintScreen";
                case "leftctrl":
                    return "LeftCtrl";
                case "rightctrl":
                    return "RightCtrl";
                case "leftshift":
                    return "LeftShift";
                case "rightshift":
                    return "RightShift";
                case "leftalt":
                    return "LeftAlt";
                case "rightalt":
                    return "RightAlt";
                case "lwin":
                    return "LWin";
                case "rwin":
                    return "RWin";
            }

            // Одиночные символы — либо цифры («1» → Key.D1), либо буквы клавиш
            // («A»…«Z», «F5» длины больше) либо отображаемые имена Oem-клавиш
            // из Hotbox (KeyToDisplay).
            if (t.Length == 1)
            {
                var c = t[0];
                if (c >= '0' && c <= '9')
                    return "D" + c;

                var isAsciiLetter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
                if (isAsciiLetter)
                    return c.ToString();

                switch (c)
                {
                    case '+':
                    case '=':
                        return "OemPlus";
                    case '-':
                        return "OemMinus";
                    case ',':
                        return "OemComma";
                    case '.':
                        return "OemPeriod";
                    case '?':
                        return "OemQuestion";
                    case '[':
                        return "OemOpenBrackets";
                    case ']':
                        return "OemCloseBrackets";
                    case '"':
                        return "OemQuotes";
                    case ';':
                        return "OemSemicolon";
                    case '\\':
                        return "OemBackslash";
                    case '|':
                        return "OemPipe";
                    case '~':
                        return "OemTilde";
                }

                return null;
            }

            // «NumPad1»…«NumPad9» и «NumPad0» — как записывает HotkeyBox.
            if (lower.StartsWith("numpad", StringComparison.Ordinal) && lower.Length == 7 &&
                lower[6] >= '0' && lower[6] <= '9')
                return "NumPad" + lower[6];

            // Клавиши-модификаторы не являются самостоятельной клавишей жеста.
            if (lower is "ctrl" or "control" or "shift" or "alt" or "win" or "windows" or "meta")
                return null;

            // Имена клавиш WPF/Avalonia состоят из латинских букв и цифр («F9»,
            // «D5», «OemPlus», «Space», «Add»). Клавиши из других раскладок —
            // например кириллическая «П» — не являются именами клавиш: такая
            // запись не может быть валидным жестом и совпадения не даёт.
            for (var i = 0; i < t.Length; i++)
            {
                var c = t[i];
                var isAsciiLetter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
                var isDigit = c >= '0' && c <= '9';
                if (!isAsciiLetter && !isDigit)
                    return null;
            }

            return t;
        }

        /// <summary>
        /// Печатает ли клавиша символ текста (буква, цифра, знак препинания, пробел).
        /// Используется, чтобы не отбирать у полей ввода печатающие клавиши:
        /// F1…F24, Delete/Insert/Back, стрелки и навигация символа не печатают и
        /// могут работать как горячие и при фокусе в тексте.
        /// </summary>
        public static bool IsTextProducingKey(string? keyName)
        {
            var normalized = NormalizeKeyName(keyName);
            if (normalized is null)
                return false;

            var lower = normalized.ToLowerInvariant();

            // Функциональные, правка, навигация и системные клавиши: текст не печатают.
            if (lower.Length > 1)
            {
                if (normalized[0] == 'F' || normalized[0] == 'f')
                {
                    var isFunction = true;
                    for (var i = 1; i < normalized.Length; i++)
                    {
                        if (normalized[i] < '0' || normalized[i] > '9')
                        {
                            isFunction = false;
                            break;
                        }
                    }

                    if (isFunction)
                        return false;
                }
            }

            switch (lower)
            {
                case "delete":
                case "insert":
                case "back":
                case "escape":
                case "tab":
                case "return":
                case "enter":
                case "space":
                case "left":
                case "right":
                case "up":
                case "down":
                case "home":
                case "end":
                case "pageup":
                case "pagedown":
                case "capslock":
                case "numlock":
                case "scrolllock":
                case "pause":
                case "printscreen":
                case "snapshot":
                case "help":
                case "apps":
                case "numpad*":
                case "numpad/":
                case "numpad.":
                    return lower is "space";
                case "leftctrl":
                case "rightctrl":
                case "leftshift":
                case "rightshift":
                case "leftalt":
                case "rightalt":
                case "lwin":
                case "rwin":
                    return false;
            }

            // Буквы A-Z, цифры D0-D9, NumPad0-9, Space, Oem-символы и Add/Subtract
            // (цифровой блок при включённом NumLock) — печатают символ.
            if (IsAsciiLetter(normalized))
                return true;
            if (normalized is "add" or "subtract" or "multiply" or "divide" or "decimal")
                return true;
            if (lower.StartsWith("oem", StringComparison.Ordinal))
                return true;
            if (normalized.Length == 2 && normalized[0] == 'D' && normalized[1] >= '0' && normalized[1] <= '9')
                return true;
            if (lower.StartsWith("numpad", StringComparison.Ordinal) && lower.Length == 7 &&
                lower[6] >= '0' && lower[6] <= '9')
                return true;

            return false;
        }

        private static bool IsAsciiLetter(string s)
        {
            if (s.Length != 1)
                return false;
            var c = s[0];
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }
    }
}
