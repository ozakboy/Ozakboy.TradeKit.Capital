using System.Text;

namespace Ozakboy.TradeKit.Capital.Host;

/// <summary>
/// 從主控台讀一行,輸入時只顯示星號。
/// Reads one console line, echoing asterisks only.
/// </summary>
internal static class ConsoleSecret
{
    /// <summary>
    /// 讀一行遮罩輸入。
    /// Reads one masked line.
    /// </summary>
    /// <param name="prompt">提示文字。Prompt.</param>
    /// <returns>輸入內容(去掉前後空白)。The trimmed input.</returns>
    public static string Read(string prompt)
    {
        Console.Write(prompt);

        // 輸入被導向(由其他程式餵入)時沒有按鍵可讀,改讀一整行
        if (Console.IsInputRedirected)
        {
            string line = Console.ReadLine() ?? string.Empty;
            Console.WriteLine();
            return line.Trim();
        }

        StringBuilder buffer = new();

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString().Trim();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }
}
