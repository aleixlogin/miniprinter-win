namespace MiniPrinter.Gui.Tests;

public class ServiceTextsTests
{
    [Theory]
    [InlineData("Pending", "En cola")]
    [InlineData("PendingHeld", "Retenido")]
    [InlineData("Processing", "Imprimiendo")]
    [InlineData("ProcessingStopped", "En espera")]
    [InlineData("Completed", "Completado")]
    [InlineData("Canceled", "Cancelado")]
    [InlineData("Aborted", "Error")]
    public void Every_job_state_of_the_service_is_translated(string state, string expected) =>
        Assert.Equal(expected, ServiceTexts.State(state));

    [Fact]
    public void An_unknown_state_is_shown_as_it_came()
    {
        Assert.Equal("Whatever", ServiceTexts.State("Whatever"));
    }

    [Theory]
    [InlineData("Printed", "Impreso")]
    [InlineData("Canceled", "Cancelado")]
    [InlineData("Printing", "Imprimiendo")]
    [InlineData("Service stopped", "Servicio detenido")]
    [InlineData("Printer did not answer", "La impresora no respondió")]
    [InlineData("Printer is in use by another application", "La impresora la usa otra aplicación")]
    [InlineData("Printer needs attention: OutOfPaper", "Sin papel")]
    [InlineData("Printer needs attention: OutOfPaper, LowBattery", "Sin papel, batería baja")]
    [InlineData("Printer needs attention: Overheated", "Sobrecalentada")]
    [InlineData("Printer not available: Bluetooth is off", "Impresora no disponible: Bluetooth is off")]
    public void Every_message_of_the_service_is_translated(string message, string expected) =>
        Assert.Equal(expected, ServiceTexts.Message(message));

    [Theory]
    [InlineData("Document format image/gif is not supported.")]
    [InlineData("Something nobody foresaw")]
    public void An_unknown_message_is_shown_unchanged(string message) =>
        Assert.Equal(message, ServiceTexts.Message(message));

    [Fact]
    public void An_empty_message_is_empty()
    {
        Assert.Equal("", ServiceTexts.Message(null));
        Assert.Equal("", ServiceTexts.Message(""));
    }

    [Fact]
    public void An_unknown_alarm_name_stays_in_the_list()
    {
        Assert.Equal("sin papel, Jammed", ServiceTexts.AlarmList("OutOfPaper, Jammed"));
    }

    [Fact]
    public void A_missing_key_shows_its_name_not_a_blank()
    {
        Assert.Equal("[No.Such.Key]", Strings.Get("No.Such.Key"));
        Assert.False(Strings.Exists("No.Such.Key"));
        Assert.True(Strings.Exists("Job.State.Completed"));
    }

    [Fact]
    public void Texts_with_arguments_are_formatted()
    {
        Assert.Equal("Impresora no disponible: x", Strings.Get("Message.PrinterUnavailable", "x"));
    }

    [Fact]
    public void The_resource_file_lists_its_keys()
    {
        Assert.Contains("Job.State.Aborted", Strings.Keys);
    }
}
