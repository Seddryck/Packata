using NUnit.Framework;
using Spectre.Console;

namespace Packata.Cli.Testing;

[TestFixture]
public sealed class PreviewPresenterTests
{
    private static string Render(PreviewSample sample, int width)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors
        });
        console.Profile.Width = width;
        PreviewPresenter.Write(sample, "table", console, writer);
        return writer.ToString();
    }

    [TestCase(80)]
    [TestCase(120)]
    [TestCase(200)]
    public void Wide_preview_keeps_dates_and_headers_on_one_line_and_preserves_all_columns(int width)
    {
        var columns = new[] { "VendorID", "tpep_pickup_datetime", "tpep_dropoff_datetime", "passenger_count",
            "trip_distance", "RatecodeID", "store_and_fwd_flag", "PULocationID", "DOLocationID", "payment_type",
            "fare_amount", "extra", "mta_tax", "tip_amount", "tolls_amount", "improvement_surcharge",
            "total_amount", "congestion_surcharge", "Airport_fee", "cbd_congestion_fee", "request_source" };
        var date = new DateTimeOffset(2026, 8, 1, 0, 3, 4, TimeSpan.FromHours(2));
        var row = columns.Select(_ => (object?)1).ToArray();
        row[1] = date;
        row[2] = date.AddMinutes(4);
        var output = Render(new(columns, [row, row]), width);
        Assert.Multiple(() =>
        {
            foreach (var column in columns) Assert.That(output, Does.Contain(column));
            Assert.That(output, Does.Contain(date.ToString("O")));
            Assert.That(output, Does.Contain("Columns 1-"));
            Assert.That(output, Does.Contain("2 row(s)."));
            foreach (var line in output.Split(Environment.NewLine)) Assert.That(line.Length, Is.LessThanOrEqualTo(width));
            var dataLines = output.Split(Environment.NewLine).Where(line => line.Contains("│ 1 │") || line.Contains("| 1 |"));
            Assert.That(dataLines.Count(), Is.GreaterThan(1), "Row numbers must repeat across column groups.");
        });
    }

    [Test]
    public void Small_preview_uses_one_table_and_renders_markup_literally()
    {
        var output = Render(new(["id", "name"], [[1, "[red]Alice[/]"], [2, null]]), 80);
        Assert.That(output, Does.Contain("[red]Alice[/]").And.Contain("(null)").And.Not.Contain("Columns "));
    }

    [Test]
    public void Long_cells_are_ellipsized_without_tall_rows()
    {
        var output = Render(new(["note"], [[new string('x', 200)]]), 40);
        Assert.That(output, Does.Contain("…").Or.Contain("..."));
        Assert.That(output.Split(Environment.NewLine).Count(line => line.Contains('x')), Is.EqualTo(1));
    }

    [Test]
    public void Empty_sample_keeps_all_headers()
    {
        var output = Render(new(["first_column", "second_column", "third_column"], []), 30);
        Assert.That(output, Does.Contain("first_column").And.Contain("second_column").And.Contain("third_column").And.Contain("0 row(s)."));
    }

    [Test]
    public void Wide_unicode_cells_do_not_force_wrapping()
    {
        var output = Render(new(["name", "note"], [["東京", string.Concat(Enumerable.Repeat("界", 30))]]), 40);
        Assert.That(output.Split(Environment.NewLine).Count(line => line.Contains('界')), Is.EqualTo(1));
        Assert.That(output, Does.Contain("東京"));
    }
}
