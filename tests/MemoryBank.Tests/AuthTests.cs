using MemoryBank.Core.Auth;
using MemoryBank.Server.Auth;

namespace MemoryBank.Tests;

public class CredentialValidatorTests
{
    [Fact]
    public void Validate_Accepts_Correct_Username_And_Password()
    {
        var hash = PasswordHasher.Hash("hunter2");
        var v = new EnvCredentialValidator("alice", hash);

        Assert.True(v.Validate("alice", "hunter2"));
    }

    [Fact]
    public void Validate_Rejects_Wrong_Username()
    {
        var hash = PasswordHasher.Hash("hunter2");
        var v = new EnvCredentialValidator("alice", hash);

        Assert.False(v.Validate("bob", "hunter2"));
    }

    [Fact]
    public void Validate_Rejects_Wrong_Password()
    {
        var hash = PasswordHasher.Hash("hunter2");
        var v = new EnvCredentialValidator("alice", hash);

        Assert.False(v.Validate("alice", "wrong"));
    }

    [Fact]
    public void Validate_Rejects_Empty_Inputs()
    {
        var hash = PasswordHasher.Hash("hunter2");
        var v = new EnvCredentialValidator("alice", hash);

        Assert.False(v.Validate("", "hunter2"));
        Assert.False(v.Validate("alice", ""));
        Assert.False(v.Validate("", ""));
    }

    [Fact]
    public void ConfiguredUsername_Is_Public()
    {
        var hash = PasswordHasher.Hash("p");
        var v = new EnvCredentialValidator("alice", hash);

        Assert.Equal("alice", v.ConfiguredUsername);
    }

    [Fact]
    public void Constructor_Throws_On_Empty_Username()
    {
        var hash = PasswordHasher.Hash("p");
        Assert.Throws<ArgumentException>(() => new EnvCredentialValidator("", hash));
        Assert.Throws<ArgumentException>(() => new EnvCredentialValidator("   ", hash));
    }

    [Fact]
    public void Constructor_Throws_On_Empty_Hash()
    {
        Assert.Throws<ArgumentException>(() => new EnvCredentialValidator("alice", ""));
    }

    [Fact]
    public void FromEnvironment_Throws_When_Vars_Missing()
    {
        var savedUser = Environment.GetEnvironmentVariable(EnvCredentialValidator.UsernameEnvVar);
        var savedHash = Environment.GetEnvironmentVariable(EnvCredentialValidator.PasswordHashEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(EnvCredentialValidator.UsernameEnvVar, null);
            Environment.SetEnvironmentVariable(EnvCredentialValidator.PasswordHashEnvVar, null);
            Assert.Throws<InvalidOperationException>(() => EnvCredentialValidator.FromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvCredentialValidator.UsernameEnvVar, savedUser);
            Environment.SetEnvironmentVariable(EnvCredentialValidator.PasswordHashEnvVar, savedHash);
        }
    }

    [Fact]
    public void FromEnvironment_Loads_Vars_When_Set()
    {
        var savedUser = Environment.GetEnvironmentVariable(EnvCredentialValidator.UsernameEnvVar);
        var savedHash = Environment.GetEnvironmentVariable(EnvCredentialValidator.PasswordHashEnvVar);
        try
        {
            var hash = PasswordHasher.Hash("test-pw");
            Environment.SetEnvironmentVariable(EnvCredentialValidator.UsernameEnvVar, "tester");
            Environment.SetEnvironmentVariable(EnvCredentialValidator.PasswordHashEnvVar, hash);

            var v = EnvCredentialValidator.FromEnvironment();
            Assert.Equal("tester", v.ConfiguredUsername);
            Assert.True(v.Validate("tester", "test-pw"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvCredentialValidator.UsernameEnvVar, savedUser);
            Environment.SetEnvironmentVariable(EnvCredentialValidator.PasswordHashEnvVar, savedHash);
        }
    }
}

public class LoginPageTests
{
    [Fact]
    public void Render_Includes_All_Hidden_Fields()
    {
        var p = new LoginPageParams(
            ClientId: "abc123",
            ClientName: "Test Client",
            RedirectUri: "https://example.com/cb",
            State: "xyz",
            CodeChallenge: "challenge-value",
            CodeChallengeMethod: "S256");

        var html = LoginPage.Render(p, errorMessage: null);

        Assert.Contains("name=\"client_id\" value=\"abc123\"", html);
        Assert.Contains("name=\"redirect_uri\" value=\"https://example.com/cb\"", html);
        Assert.Contains("name=\"state\" value=\"xyz\"", html);
        Assert.Contains("name=\"code_challenge\" value=\"challenge-value\"", html);
        Assert.Contains("name=\"code_challenge_method\" value=\"S256\"", html);
        Assert.Contains("Test Client", html);
    }

    [Fact]
    public void Render_With_Error_Includes_Error_Message()
    {
        var p = new LoginPageParams("c", "Client", "/cb", "s", null, null);
        var html = LoginPage.Render(p, errorMessage: "Bad creds");

        Assert.Contains("class=\"error\"", html);
        Assert.Contains("Bad creds", html);
    }

    [Fact]
    public void Render_Without_Error_Omits_Error_Block()
    {
        var p = new LoginPageParams("c", "Client", "/cb", "s", null, null);
        var html = LoginPage.Render(p, errorMessage: null);

        Assert.DoesNotContain("class=\"error\"", html);
    }

    [Fact]
    public void Render_Escapes_HTML_In_ClientName()
    {
        var p = new LoginPageParams("c", "<script>alert(1)</script>", "/cb", "s", null, null);
        var html = LoginPage.Render(p, null);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Render_Escapes_HTML_In_Hidden_Field_Values()
    {
        var p = new LoginPageParams(
            ClientId: "\"><script>",
            ClientName: "Client",
            RedirectUri: "/cb",
            State: "s",
            CodeChallenge: null,
            CodeChallengeMethod: null);

        var html = LoginPage.Render(p, null);
        Assert.DoesNotContain("\"><script>", html);
        Assert.Contains("&quot;&gt;&lt;script&gt;", html);
    }

    [Fact]
    public void Render_Falls_Back_To_Generic_Name_When_Empty()
    {
        var p = new LoginPageParams("c", "", "/cb", "s", null, null);
        var html = LoginPage.Render(p, null);

        Assert.Contains("this client", html);
    }
}
