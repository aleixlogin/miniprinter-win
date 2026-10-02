// PDFium (Docnet) is not thread-safe and crashed the test host when PdfTests ran alongside the
// other test classes; the whole suite takes about a second, so run it sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
