using System.Collections.Generic;
using Xunit;

namespace IziProxy.Tests;

/// <summary>
/// Тесты для <see cref="VdsProfile.Validate"/> (F-08 из FutureTest.md).
/// Покрывают: пустые поля, валидные IPv4/IPv6/hostname, невалидные
/// hostname (спецсимволы, IDN), path traversal в SshKeyPath, требование
/// хотя бы одного из password/key, лимит длины имени.
/// </summary>
public class VdsProfileValidationTests
{
    [Fact]
    public void Validate_AllFieldsValid_ReturnsEmptyErrors()
    {
        var p = new VdsProfile
        {
            Name = "My VDS #1",
            Host = "vds.example.com",
            Username = "root",
            Password = "secret"
        };
        Assert.Empty(p.Validate());
    }

    [Fact]
    public void Validate_AllFieldsValid_IPv4_ReturnsEmpty()
    {
        var p = new VdsProfile
        {
            Name = "test",
            Host = "192.168.1.1",
            Username = "admin",
            Password = "x"
        };
        Assert.Empty(p.Validate());
    }

    [Fact]
    public void Validate_AllFieldsValid_IPv6_ReturnsEmpty()
    {
        var p = new VdsProfile
        {
            Name = "test",
            Host = "::1",
            Username = "root",
            Password = "x"
        };
        Assert.Empty(p.Validate());
    }

    [Fact]
    public void Validate_AllFieldsValid_IPv6Full_ReturnsEmpty()
    {
        var p = new VdsProfile
        {
            Name = "test",
            Host = "2001:db8::1",
            Username = "root",
            Password = "x"
        };
        Assert.Empty(p.Validate());
    }

    [Fact]
    public void Validate_AllFieldsValid_SshKeyOnly_ReturnsEmpty()
    {
        var p = new VdsProfile
        {
            Name = "test",
            Host = "vds.com",
            Username = "deploy",
            Password = "",
            SshKeyPath = "/home/deploy/.ssh/id_rsa"
        };
        Assert.Empty(p.Validate());
    }

    [Fact]
    public void Validate_AllFieldsValid_TildePath_ReturnsEmpty()
    {
        // SSH.NET и наш SSH.cs (ISS-06 fix в плане) обрабатывают ~/.
        // Текущая валидация принимает оба варианта.
        var p = new VdsProfile
        {
            Name = "test",
            Host = "vds.com",
            Username = "deploy",
            Password = "",
            SshKeyPath = "~/.ssh/id_rsa"
        };
        Assert.Empty(p.Validate());
    }

    [Fact]
    public void Validate_EmptyName_HasError()
    {
        var p = new VdsProfile
        {
            Name = "",
            Host = "vds.com",
            Username = "root",
            Password = "x"
        };
        var errors = p.Validate();
        Assert.Contains(errors, e => e.Contains("Имя профиля"));
    }

    [Fact]
    public void Validate_WhitespaceOnlyName_HasError()
    {
        var p = new VdsProfile
        {
            Name = "   ",
            Host = "vds.com",
            Username = "root",
            Password = "x"
        };
        var errors = p.Validate();
        Assert.Contains(errors, e => e.Contains("Имя профиля"));
    }

    [Fact]
    public void Validate_NameTooLong_HasError()
    {
        var p = new VdsProfile
        {
            Name = new string('a', 65), // 65 > 64
            Host = "vds.com",
            Username = "root",
            Password = "x"
        };
        var errors = p.Validate();
        Assert.Contains(errors, e => e.Contains("слишком длинное"));
    }

    [Fact]
    public void Validate_EmptyHost_HasError()
    {
        var p = new VdsProfile { Name = "x", Username = "root", Password = "x" };
        var errors = p.Validate();
        Assert.Contains(errors, e => e.Contains("IP/Host"));
    }

    [Fact]
    public void Validate_InvalidHost_NotAnIpOrHostname()
    {
        // Недопустимые символы в hostname (RFC 1123): подчёркивание, ! и т.д.
        var invalidHosts = new[]
        {
            "host with spaces",
            "host_with_underscore",
            "host!invalid",
            "-leading-dash.com",
            "trailing-dash-.com",
            "under_score.com",
        };

        foreach (var host in invalidHosts)
        {
            var p = new VdsProfile
            {
                Name = "x",
                Host = host,
                Username = "root",
                Password = "x"
            };
            var errors = p.Validate();
            Assert.True(errors.Any(e => e.Contains("Некорректный IP или hostname")),
                $"Host '{host}' should be invalid but got errors: [{string.Join(", ", errors)}]");
        }
    }

    [Fact]
    public void Validate_ValidHostnames_Accepted()
    {
        var validHosts = new[]
        {
            "vds.example.com",
            "localhost",
            "sub.domain.example.com",
            "a.b.c.d.e.f.g.h.i.j.k.l.m.n.o.p.example.com",
            "1.2.3.4",
            "10.0.0.1",
            "::1",
            "2001:db8::1",
            "fe80::1",
        };

        foreach (var host in validHosts)
        {
            var p = new VdsProfile
            {
                Name = "x",
                Host = host,
                Username = "root",
                Password = "x"
            };
            Assert.True(p.Validate().Count == 0, $"Host '{host}' should be valid");
        }
    }

    [Fact]
    public void Validate_EmptyUsername_HasError()
    {
        var p = new VdsProfile
        {
            Name = "x",
            Host = "vds.com",
            Username = "",
            Password = "x"
        };
        var errors = p.Validate();
        Assert.Contains(errors, e => e.Contains("Username"));
    }

    [Fact]
    public void Validate_InvalidUsername_NotPosix()
    {
        // POSIX username: [a-z_][a-z0-9_-]*. Невалидные примеры:
        // - начинается с цифры
        // - содержит заглавные
        // - содержит спецсимволы
        // - слишком длинный (>32)
        var invalidUsernames = new[]
        {
            "1user",        // начинается с цифры
            "USER",         // заглавные
            "user@host",    // @
            "user.name",    // .
            "user name",    // пробел
            new string('a', 33), // > 32
        };

        foreach (var username in invalidUsernames)
        {
            var p = new VdsProfile
            {
                Name = "x",
                Host = "vds.com",
                Username = username,
                Password = "x"
            };
            var errors = p.Validate();
            Assert.True(errors.Any(e => e.Contains("Некорректный username")),
                $"Username '{username}' should be invalid but got errors: [{string.Join(", ", errors)}]");
        }
    }

    [Fact]
    public void Validate_ValidUsernames_Accepted()
    {
        var validUsernames = new[]
        {
            "root",
            "deploy",
            "_internal",
            "user-name",
            "user_name",  // подчёркивание в середине — ок по POSIX
            "a",          // один символ
            "a1b2c3",
        };

        foreach (var username in validUsernames)
        {
            var p = new VdsProfile
            {
                Name = "x",
                Host = "vds.com",
                Username = username,
                Password = "x"
            };
            Assert.True(p.Validate().Count == 0, $"Username '{username}' should be valid");
        }
    }

    [Fact]
    public void Validate_NoPasswordNoKey_HasError()
    {
        var p = new VdsProfile
        {
            Name = "x",
            Host = "vds.com",
            Username = "root",
            Password = "",
            SshKeyPath = ""
        };
        var errors = p.Validate();
        Assert.Contains(errors, e => e.Contains("пароль") && e.Contains("ключ"));
    }

    [Fact]
    public void Validate_PathTraversalInSshKeyPath_HasError()
    {
        var p = new VdsProfile
        {
            Name = "x",
            Host = "vds.com",
            Username = "root",
            SshKeyPath = "/home/deploy/../../etc/passwd"
        };
        var errors = p.Validate();
        Assert.Contains(errors, e => e.Contains("path traversal"));
    }

    [Fact]
    public void Validate_RelativeSshKeyPath_HasError()
    {
        // Относительные пути опасны — резолвятся относительно CWD
        // процесса, на Linux под AppImage/snap/flatpak CWD непредсказуем.
        var p = new VdsProfile
        {
            Name = "x",
            Host = "vds.com",
            Username = "root",
            SshKeyPath = "id_rsa"
        };
        var errors = p.Validate();
        Assert.Contains(errors, e => e.Contains("абсолютным"));
    }

    [Fact]
    public void Validate_MultipleErrors_AllReported()
    {
        // Все поля невалидны — пользователь должен увидеть все ошибки,
        // а не только первую.
        var p = new VdsProfile
        {
            Name = "",
            Host = "bad host!",
            Username = "1bad",
            Password = "",
            SshKeyPath = ""
        };
        var errors = p.Validate();

        Assert.True(errors.Count >= 4, $"Expected at least 4 errors, got {errors.Count}: [{string.Join(", ", errors)}]");
        Assert.Contains(errors, e => e.Contains("Имя профиля"));
        Assert.Contains(errors, e => e.Contains("IP/Host") || e.Contains("IP или hostname"));
        Assert.Contains(errors, e => e.Contains("username"));  // case-insensitive: ошибка содержит 'Некорректный username'
        Assert.Contains(errors, e => e.Contains("пароль") && e.Contains("ключ"));
    }
}
