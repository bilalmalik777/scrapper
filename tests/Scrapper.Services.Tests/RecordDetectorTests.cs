using FluentAssertions;
using HtmlAgilityPack;
using Scrapper.Models.DTOs;
using Scrapper.Services.Implementations;
using Xunit;

namespace Scrapper.Services.Tests;

public class RecordDetectorTests
{
    private readonly RecordDetector _sut = new(new FieldInferenceEngine());

    [Fact]
    public void DetectRecords_RepeatedCards_ReturnsAllCards()
    {
        const string html = """
            <html><body>
            <div class="search-result-card"><h2>Dr John Smith</h2><span>Manchester</span></div>
            <div class="search-result-card"><h2>Dr Sarah Jones</h2><span>London</span></div>
            <div class="search-result-card"><h2>Dr David Brown</h2><span>Birmingham</span></div>
            </body></html>
            """;
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var records = _sut.DetectRecords(doc.DocumentNode);

        records.Should().NotBeNull();
        records!.Should().HaveCount(3);
    }

    [Fact]
    public void DetectRecords_NoRepeatedStructure_ReturnsNull()
    {
        const string html = "<html><body><div class='unique-one'>Only content</div></body></html>";
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var records = _sut.DetectRecords(doc.DocumentNode);

        records.Should().BeNull();
    }

    [Fact]
    public void DetectRecords_IgnoresSparseNavLinks_PrefersRicherCards()
    {
        const string html = """
            <html><body>
            <nav>
              <a class="nav-link" href="#">Home</a>
              <a class="nav-link" href="#">About</a>
              <a class="nav-link" href="#">Contact</a>
              <a class="nav-link" href="#">Blog</a>
            </nav>
            <div class="specialist-card"><h2>Dr John Smith</h2><p>Sport & Exercise Medicine Doctor, Manchester, £150 consultation fee</p></div>
            <div class="specialist-card"><h2>Dr Sarah Jones</h2><p>Sport & Exercise Medicine Doctor, London, £180 consultation fee</p></div>
            <div class="specialist-card"><h2>Dr David Brown</h2><p>Sport & Exercise Medicine Doctor, Birmingham, £125 consultation fee</p></div>
            </body></html>
            """;
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var records = _sut.DetectRecords(doc.DocumentNode);

        records.Should().NotBeNull();
        records!.Should().HaveCount(3);
        records!.All(r => r.GetAttributeValue("class", "") == "specialist-card").Should().BeTrue();
    }

    [Fact]
    public void DetectRecords_WithFields_PrefersGroupThatActuallyContainsRequestedFields()
    {
        // A detail page: one genuine content section (Fees) appears once, but an unrelated
        // widget (skill/rating tags) repeats far more often and would otherwise win on pure
        // repetition count.
        const string html = """
            <html><body>
            <div class="skill-tag"><span class="skill-name">Sports Injuries</span></div>
            <div class="skill-tag"><span class="skill-name">Knee Pain</span></div>
            <div class="skill-tag"><span class="skill-name">Back Pain</span></div>
            <div class="skill-tag"><span class="skill-name">Shoulder Pain</span></div>
            <div class="skill-tag"><span class="skill-name">Elbow Pain</span></div>
            <h1>Dr John Smith</h1>
            <div class="fees-section"><p class="fee-label">New appointment</p><span class="fee-amount">£425</span></div>
            </body></html>
            """;
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var fields = new List<FieldDefinitionDto> { new() { Name = "Fees" } };

        var records = _sut.DetectRecords(doc.DocumentNode, fields);

        // Neither group contains a "Fees" match well enough (the fees section only appears
        // once, so it can't repeat into multiple records) — the right outcome is "no
        // records detected", not "5 skill tags mistaken for records".
        records.Should().BeNull();
    }

    [Fact]
    public void DetectRecords_WithFields_PicksCardsThatContainTheFields()
    {
        const string html = """
            <html><body>
            <div class="unrelated-tag">Tag A</div>
            <div class="unrelated-tag">Tag B</div>
            <div class="unrelated-tag">Tag C</div>
            <div class="unrelated-tag">Tag D</div>
            <div class="specialist-card"><h2>Dr John Smith</h2><div class="fee">£150</div></div>
            <div class="specialist-card"><h2>Dr Sarah Jones</h2><div class="fee">£180</div></div>
            <div class="specialist-card"><h2>Dr David Brown</h2><div class="fee">£125</div></div>
            </body></html>
            """;
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var fields = new List<FieldDefinitionDto> { new() { Name = "Name" }, new() { Name = "Fees" } };

        var records = _sut.DetectRecords(doc.DocumentNode, fields);

        records.Should().NotBeNull();
        records!.Should().HaveCount(3);
        records!.All(r => r.GetAttributeValue("class", "") == "specialist-card").Should().BeTrue();
    }

    [Fact]
    public void DetectRecords_ManyDetailOnlyFieldsAlongsideRealListingFields_StillPicksTheCard()
    {
        // Regression: requesting several fields that only exist on a detail/profile page
        // (Qualifications, Insurers, ...) alongside fields that genuinely appear on the
        // listing (Name, Location) must not dilute the listing fields' strong signal below
        // the acceptance threshold just because most of the other fields score zero here —
        // those are expected to be filled in later by profile crawling, not by the listing.
        const string html = """
            <html><body>
            <div class="specialist-card"><h2>Dr John Smith</h2><div class="location">SE1 9BS</div></div>
            <div class="specialist-card"><h2>Dr Sarah Jones</h2><div class="location">W1G 0AR</div></div>
            <div class="specialist-card"><h2>Dr David Brown</h2><div class="location">HA1 3RX</div></div>
            </body></html>
            """;
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var fields = new List<FieldDefinitionDto>
        {
            new() { Name = "Name" },
            new() { Name = "Location" },
            new() { Name = "Qualifications" },
            new() { Name = "Insurers" },
            new() { Name = "Languages" },
            new() { Name = "Registration" },
            new() { Name = "Age Ranges" },
        };

        var records = _sut.DetectRecords(doc.DocumentNode, fields);

        records.Should().NotBeNull();
        records!.Should().HaveCount(3);
    }

    [Fact]
    public void DetectRecords_CompoundTestidContainingNameWord_DoesNotFalselyMatchNameField()
    {
        // Regression: a "keyword-name" style testid (common naming convention for "the name
        // property of a keyword/tag", unrelated to a person's name) must not count as
        // evidence for a "Name" field just because the word "name" appears in it once
        // hyphens are normalized to spaces.
        const string html = """
            <html><body>
            <div class="tag-item"><span data-testid="keyword-name">Sports Injuries</span></div>
            <div class="tag-item"><span data-testid="keyword-name">Knee Pain</span></div>
            <div class="tag-item"><span data-testid="keyword-name">Back Pain</span></div>
            <div class="tag-item"><span data-testid="keyword-name">Shoulder Pain</span></div>
            </body></html>
            """;
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var fields = new List<FieldDefinitionDto> { new() { Name = "Name" } };

        var records = _sut.DetectRecords(doc.DocumentNode, fields);

        records.Should().BeNull();
    }
}
