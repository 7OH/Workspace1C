using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты разбора аргументов командной строки CLI (<see cref="CliArgs.Parse"/>):
/// формы --run/--run=, модификатор --designer, команда --list, глобальные флаги
/// --json/--profile и отсутствие команд. Сам обработчик (доступ к DI, запуск
/// процессов) в тестах не гоняется.
/// </summary>
public sealed class CliArgsTests
{
    [Fact]
    public void Parse_EmptyArgs_ReturnsNull()
    {
        Assert.Null(CliArgs.Parse(null));
        Assert.Null(CliArgs.Parse(Array.Empty<string>()));
    }

    [Fact]
    public void Parse_RunWithName_TakesNextArgument()
    {
        var result = CliArgs.Parse(new[] { "--run", "Бухгалтерия" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Run, result.Kind);
        Assert.Equal("Бухгалтерия", result.Target);
        Assert.False(result.Options.Designer);
    }

    [Fact]
    public void Parse_RunEqualsForm_ReadsValue()
    {
        var result = CliArgs.Parse(new[] { "--run=Зарплата" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Run, result.Kind);
        Assert.Equal("Зарплата", result.Target);
    }

    [Fact]
    public void Parse_RunWithDesignerFlag_SetsDesigner()
    {
        var result = CliArgs.Parse(new[] { "--run", "База", "--designer" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Run, result.Kind);
        Assert.Equal("База", result.Target);
        Assert.True(result.Options.Designer);
    }

    [Fact]
    public void Parse_DesignerEqualsForm_IsNotModifier()
    {
        // Форма --designer=... — команда проводника (путь), а не модификатор;
        // она обрабатывается ExplorerCommandLine и до CLI не доходит.
        var result = CliArgs.Parse(new[] { "--run", "База", "--designer=C:\\ib\\1CD" });

        Assert.NotNull(result);
        Assert.False(result.Options.Designer);
    }

    [Fact]
    public void Parse_ListOnly_ReturnsList()
    {
        var result = CliArgs.Parse(new[] { "--list" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.List, result.Kind);
    }

    [Fact]
    public void Parse_ListWithRun_ListWins()
    {
        // Обе команды в одном запуске: --list без цели, --run ищется, но List
        // тоже валиден. Парсер фиксирует обе; обработчик отдаёт приоритет List.
        var result = CliArgs.Parse(new[] { "--list", "--run", "База" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.List, result.Kind);
    }

    [Fact]
    public void Parse_RunWithoutTarget_ReturnsNull()
    {
        Assert.Null(CliArgs.Parse(new[] { "--run" }));
        Assert.Null(CliArgs.Parse(new[] { "--designer" }));
    }

    [Fact]
    public void Parse_UnrelatedArgs_ReturnsNull()
    {
        Assert.Null(CliArgs.Parse(new[] { "--register", @"C:\ib\base.1CD" }));
        Assert.Null(CliArgs.Parse(new[] { "/RestoreIB" }));
        // --run-task принадлежит функции 2 (цикл 0.3.9.167–171) и обрабатывается
        // отдельным headless-входом — в текущей сборке это обычный запуск.
        Assert.Null(CliArgs.Parse(new[] { "--run-task", "abc123", "--profile", "p1" }));
    }

    [Fact]
    public void Parse_QuotedValueViaEquals_StripsQuotes()
    {
        var result = CliArgs.Parse(new[] { "--run=\"Моя база 1\"" });

        Assert.NotNull(result);
        Assert.Equal("Моя база 1", result.Target);
    }

    [Fact]
    public void Parse_BaseAlias_WorksLikeRun()
    {
        var result = CliArgs.Parse(new[] { "--base", "Отчётность" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Run, result.Kind);
        Assert.Equal("Отчётность", result.Target);
    }

    [Fact]
    public void Parse_JsonFlag_SetsJsonOption()
    {
        var result = CliArgs.Parse(new[] { "--list", "--json" });
        var resultEquals = CliArgs.Parse(new[] { "--list", "--json=true" });

        Assert.NotNull(result);
        Assert.True(result.Options.Json);
        Assert.NotNull(resultEquals);
        Assert.True(resultEquals.Options.Json);
    }

    [Fact]
    public void Parse_ProfileSeparateForm_ReadsValue()
    {
        var result = CliArgs.Parse(new[] { "--run", "База", "--profile", "p-001" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Run, result.Kind);
        Assert.Equal("p-001", result.Options.Profile);
    }

    [Fact]
    public void Parse_ProfileEqualsForm_ReadsValue()
    {
        var result = CliArgs.Parse(new[] { "--list", "--profile=p-001" });

        Assert.NotNull(result);
        Assert.Equal("p-001", result.Options.Profile);
    }

    [Fact]
    public void Parse_ProfileWithoutCommand_ReturnsNull()
    {
        // --profile сам по себе не является командой: обычный запуск приложения.
        Assert.Null(CliArgs.Parse(new[] { "--profile", "p-001" }));
        Assert.Null(CliArgs.Parse(new[] { "--json" }));
    }

    [Fact]
    public void Parse_JsonWithRun_SetsBoth()
    {
        var result = CliArgs.Parse(new[] { "--run", "База", "--json", "--designer" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.Run, result.Kind);
        Assert.True(result.Options.Json);
        Assert.True(result.Options.Designer);
    }

    // ---------- Telegram-мост (выпуск 1.0.0.36): --telegram-send / --telegram-confirm ----------

    [Fact]
    public void Parse_TelegramSend_ReadsTextTitleAndKind()
    {
        var result = CliArgs.Parse(new[] { "--telegram-send", "--text", "Сборка готова", "--title", "СБОРКА", "--kind", "success" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.TelegramSend, result.Kind);
        Assert.Equal("Сборка готова", result.Options.Text);
        Assert.Equal("СБОРКА", result.Options.Title);
        Assert.Equal("success", result.Options.Kind);
    }

    [Fact]
    public void Parse_TelegramSend_EqualsForms_AndJson()
    {
        var result = CliArgs.Parse(new[] { "--telegram-send", "--text=Тест", "--json" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.TelegramSend, result.Kind);
        Assert.Equal("Тест", result.Options.Text);
        Assert.True(result.Options.Json);
    }

    [Fact]
    public void Parse_TelegramSend_WithoutText_ReturnsError()
    {
        var result = CliArgs.Parse(new[] { "--telegram-send" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.TelegramSend, result.Kind);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_TelegramConfirm_ReadsPromptButtonsAndTimeout()
    {
        var result = CliArgs.Parse(new[]
        {
            "--telegram-confirm", "--prompt", "Публикуем релиз?", "--button", "Да, публиковать", "--button", "Нет", "--timeout", "120", "--json"
        });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.TelegramConfirm, result.Kind);
        Assert.Equal("Публикуем релиз?", result.Options.Prompt);
        Assert.Equal(2, result.Options.Buttons.Count);
        Assert.Equal("Да, публиковать", result.Options.Buttons[0]);
        Assert.Equal("Нет", result.Options.Buttons[1]);
        Assert.Equal(120, result.Options.TimeoutSeconds);
        Assert.True(result.Options.Json);
    }

    [Fact]
    public void Parse_TelegramConfirm_WithoutPrompt_ReturnsError()
    {
        var result = CliArgs.Parse(new[] { "--telegram-confirm", "--button", "ОК" });

        Assert.NotNull(result);
        Assert.Equal(CliCommandKind.TelegramConfirm, result.Kind);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_TelegramCommands_ProfileAndKindPreserved()
    {
        var send = CliArgs.Parse(new[] { "--telegram-send", "--text", "x", "--profile", "p-001", "--kind=error" });
        Assert.NotNull(send);
        Assert.Equal("p-001", send.Options.Profile);
        Assert.Equal("error", send.Options.Kind);

        var confirm = CliArgs.Parse(new[] { "--telegram-confirm", "--prompt", "Вопрос?", "--timeout=30" });
        Assert.NotNull(confirm);
        Assert.Equal(30, confirm.Options.TimeoutSeconds);
    }

    [Fact]
    public void CommandName_UsesUnderscoreForTelegramCommands()
    {
        Assert.Equal("telegram_send", CliCommands.CommandName(CliCommandKind.TelegramSend));
        Assert.Equal("telegram_confirm", CliCommands.CommandName(CliCommandKind.TelegramConfirm));
        Assert.Equal("list", CliCommands.CommandName(CliCommandKind.List));
        Assert.Equal("run", CliCommands.CommandName(CliCommandKind.Run));
    }
}