using Drm.Agent.Core;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class BulkRecipientParserTests
{
    [Fact]
    public void Returns_empty_for_null_whitespace_or_no_at_sign()
    {
        BulkRecipientParser.Parse(null!).Should().BeEmpty();
        BulkRecipientParser.Parse("").Should().BeEmpty();
        BulkRecipientParser.Parse("   ").Should().BeEmpty();
        BulkRecipientParser.Parse("not-an-email").Should().BeEmpty();
    }

    [Fact]
    public void Returns_single_recipient_when_only_one_address_provided()
    {
        var result = BulkRecipientParser.Parse("malee@xyz.com");
        result.Should().ContainSingle().Which.Should().Be("malee@xyz.com");
    }

    [Theory]
    [InlineData("alice@a.com, bob@b.com, carol@c.com")]
    [InlineData("alice@a.com; bob@b.com; carol@c.com")]
    [InlineData("alice@a.com\nbob@b.com\ncarol@c.com")]
    [InlineData("alice@a.com\r\nbob@b.com\r\ncarol@c.com")]
    [InlineData("alice@a.com,bob@b.com;carol@c.com")]
    public void Splits_on_comma_semicolon_or_newline(string input)
    {
        BulkRecipientParser.Parse(input)
            .Should().Equal("alice@a.com", "bob@b.com", "carol@c.com");
    }

    [Fact]
    public void Trims_whitespace_around_each_address()
    {
        BulkRecipientParser.Parse("  alice@a.com  ,\tbob@b.com\t,  carol@c.com  ")
            .Should().Equal("alice@a.com", "bob@b.com", "carol@c.com");
    }

    [Fact]
    public void Drops_segments_with_no_at_sign()
    {
        BulkRecipientParser.Parse("alice@a.com, not-an-email, bob@b.com, ")
            .Should().Equal("alice@a.com", "bob@b.com");
    }

    [Fact]
    public void Deduplicates_case_insensitively_keeping_first_spelling()
    {
        BulkRecipientParser.Parse("alice@a.com, Bob@B.com, BOB@b.com, alice@A.com")
            .Should().Equal("alice@a.com", "Bob@B.com");
    }

    [Fact]
    public void Survives_excel_paste_with_tab_separators()
    {
        // Pasting a column from Excel can include tabs + carriage returns.
        BulkRecipientParser.Parse("alice@a.com\tbob@b.com\tcarol@c.com")
            .Should().Equal("alice@a.com", "bob@b.com", "carol@c.com");
    }
}
