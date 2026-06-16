using System;
using System.Collections.Generic;
using Xunit;

namespace IziProxy.Tests;

/// <summary>
/// Тесты для <see cref="PasswordMasker"/> (ISS-01).
/// Покрывают: пустые/null значения, отсутствие вхождений, одно/несколько
/// вхождений, регистр, спецсимволы, длинные пароли, форвардинг в
/// вложенный progress.
/// </summary>
public class PasswordMaskerTests
{
    // Минимальный тестовый IProgress<string>, запоминает все вызовы Report.
    private sealed class CapturingProgress : IProgress<string>
    {
        public List<string> Reported { get; } = new();
        public void Report(string value) => Reported.Add(value);
    }

    [Fact]
    public void Mask_NullInner_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new PasswordMasker(null!, "secret"));
    }

    [Fact]
    public void Mask_EmptyPassword_PassesThroughUnchanged()
    {
        var inner = new CapturingProgress();
        var masker = new PasswordMasker(inner, "");

        masker.Report("hello world");
        masker.Report("password=12345");

        Assert.Equal(new[] { "hello world", "password=12345" }, inner.Reported);
    }

    [Fact]
    public void Mask_NullPassword_TreatedAsEmpty()
    {
        var inner = new CapturingProgress();
        var masker = new PasswordMasker(inner, null!);

        masker.Report("free text without anything");

        Assert.Equal(new[] { "free text without anything" }, inner.Reported);
    }

    [Fact]
    public void Mask_EmptyMessage_PassesThrough()
    {
        var inner = new CapturingProgress();
        var masker = new PasswordMasker(inner, "secret");

        masker.Report("");

        Assert.Equal(new[] { "" }, inner.Reported);
    }

    [Fact]
    public void Mask_NoOccurrence_PassesThrough()
    {
        var inner = new CapturingProgress();
        var masker = new PasswordMasker(inner, "secret");

        masker.Report("connection successful, all good");
        masker.Report("user authenticated as root");

        Assert.Equal(new[] {
            "connection successful, all good",
            "user authenticated as root"
        }, inner.Reported);
    }

    [Fact]
    public void Mask_SingleOccurrence_ReplacesOnce()
    {
        var inner = new CapturingProgress();
        var masker = new PasswordMasker(inner, "MyP@ssw0rd");

        masker.Report("login failed: invalid MyP@ssw0rd for user root");

        Assert.Equal("login failed: invalid *** for user root", inner.Reported[0]);
    }

    [Fact]
    public void Mask_MultipleOccurrences_ReplacesAll()
    {
        var inner = new CapturingProgress();
        var masker = new PasswordMasker(inner, "abc");

        masker.Report("abc->abc->abc done");

        Assert.Equal("***->***->*** done", inner.Reported[0]);
    }

    [Fact]
    public void Mask_CaseSensitive_DoesNotReplaceDifferentCase()
    {
        var inner = new CapturingProgress();
        var masker = new PasswordMasker(inner, "Pass");

        // Вхождение "pass" в нижнем регистре НЕ должно заменяться — иначе
        // можно по ошибке замаскировать безобидный текст, содержащий
        // подстроку пароля в другом регистре.
        masker.Report("password is pass and PASS and Pass");

        Assert.Equal("password is pass and PASS and ***", inner.Reported[0]);
    }

    [Fact]
    public void Mask_LongPasswordWithSpecialChars_Replaces()
    {
        var inner = new CapturingProgress();
        const string pwd = "P@$$w0rd!#%^&*()_+-={}[]|\\:;\"'<>,.?/~`";
        var masker = new PasswordMasker(inner, pwd);

        masker.Report($"auth: {pwd} (stored)");

        var expected = "auth: *** (stored)";
        Assert.Equal(expected, inner.Reported[0]);
    }

    [Fact]
    public void Mask_UnicodePassword_Replaces()
    {
        var inner = new CapturingProgress();
        const string pwd = "пароль123";
        var masker = new PasswordMasker(inner, pwd);

        masker.Report("user entered пароль123 and got error");

        Assert.Equal("user entered *** and got error", inner.Reported[0]);
    }

    [Fact]
    public void Mask_AdjacentOccurrences_ReplacesEachSeparately()
    {
        var inner = new CapturingProgress();
        var masker = new PasswordMasker(inner, "ab");

        // "abXabXab" — три непересекающихся вхождения "ab". .NET Replace
        // сканирует слева направо и заменяет каждое вхождение отдельно
        // (non-overlapping). Mask = "***" (3 символа), итого:
        // 3 * "***" + 2 * "X" = 9 + 2 = 11 символов.
        masker.Report("abXabXab");

        Assert.Equal("***X***X***", inner.Reported[0]);
    }

    [Fact]
    public void Mask_ForwardsMultipleCallsInOrder()
    {
        var inner = new CapturingProgress();
        var masker = new PasswordMasker(inner, "secret");

        masker.Report("first secret message");
        masker.Report("second secret message");
        masker.Report("third (no pwd) message");

        Assert.Equal(new[] {
            "first *** message",
            "second *** message",
            "third (no pwd) message"
        }, inner.Reported);
    }

    [Fact]
    public void Mask_PasswordAsSubstring_DoesNotAffectUnrelatedText()
    {
        var inner = new CapturingProgress();
        // Пароль "api_key" — может случайно встретиться в тексте про API.
        // Заменяем ТОЛЬКО точные вхождения пароля, не все упоминания API.
        var masker = new PasswordMasker(inner, "api_key=xyz");

        masker.Report("api_key=xyz validated");
        masker.Report("api_key is the configuration field");
        masker.Report("using api_key=xyz to authenticate");

        Assert.Equal(new[] {
            "*** validated",
            "api_key is the configuration field",
            "using *** to authenticate"
        }, inner.Reported);
    }
}
