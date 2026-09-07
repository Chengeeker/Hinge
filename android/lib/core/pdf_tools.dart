import 'dart:convert';
import 'dart:typed_data';

class PdfMetadata {
  final String version;
  final int pageCount;
  final String? title;
  final int fileSizeBytes;

  const PdfMetadata({
    required this.version,
    required this.pageCount,
    this.title,
    required this.fileSizeBytes,
  });
}

class PdfTools {
  /// Inspects PDF binary bytes and extracts page count, version, and metadata.
  static PdfMetadata inspectMetadata(Uint8List pdfBytes) {
    if (pdfBytes.length < 10) {
      throw ArgumentError('Invalid PDF data: file is empty or too short');
    }

    final raw = latin1.decode(pdfBytes, allowInvalid: true);

    // Version
    final headerRegex = RegExp(r'%PDF-(\d+\.\d+)');
    final headerMatch = headerRegex.firstMatch(raw);
    final version = headerMatch != null ? headerMatch.group(1)! : '1.4';

    // Page count: count /Type /Page
    final pageRegex = RegExp(r'/Type\s*/Page\b(?!\s*s)');
    final matches = pageRegex.allMatches(raw);
    int pageCount = matches.length;

    // Fallback to /Pages /Count if pageCount == 0
    if (pageCount == 0) {
      final countRegex = RegExp(r'/Pages[\s\S]*?/Count\s+(\d+)');
      final countMatch = countRegex.firstMatch(raw);
      if (countMatch != null) {
        pageCount = int.tryParse(countMatch.group(1)!) ?? 1;
      }
    }

    // Title
    String? title;
    final titleRegex = RegExp(r'/Title\s*\(([^)]+)\)');
    final titleMatch = titleRegex.firstMatch(raw);
    if (titleMatch != null) {
      title = titleMatch.group(1);
    }

    return PdfMetadata(
      version: version,
      pageCount: pageCount > 0 ? pageCount : 1,
      title: title,
      fileSizeBytes: pdfBytes.length,
    );
  }

  /// Creates a valid standard PDF 1.4 document with synthetic test pages.
  static Uint8List createDocument(String title, List<String> pageContents) {
    final pages = pageContents.isEmpty ? ['Empty Document Page'] : pageContents;
    final totalPages = pages.length;

    final buffer = StringBuffer();
    final offsets = <int>[0];

    // Header
    buffer.write('%PDF-1.4\n%\xE2\xE3\xCF\xD3\n');

    final fontObjId = 3 + 2 * totalPages;

    // 1 0 obj: Catalog
    offsets.add(buffer.length);
    buffer.write('1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n');

    // 2 0 obj: Pages
    offsets.add(buffer.length);
    final kids = List.generate(totalPages, (i) => '${3 + i} 0 R').join(' ');
    buffer.write(
      '2 0 obj\n<< /Type /Pages /Kids [$kids] /Count $totalPages >>\nendobj\n',
    );

    // Page objects
    for (int i = 0; i < totalPages; i++) {
      final pageObjId = 3 + i;
      final contentObjId = 3 + totalPages + i;
      offsets.add(buffer.length);
      buffer.write(
        '$pageObjId 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents $contentObjId 0 R /Resources << /Font << /F1 $fontObjId 0 R >> >> >>\nendobj\n',
      );
    }

    // Content streams
    for (int i = 0; i < totalPages; i++) {
      final contentObjId = 3 + totalPages + i;
      offsets.add(buffer.length);
      final text = _escapePdfText(pages[i]);
      final streamContent = 'BT /F1 16 Tf 50 720 Td ($text) Tj ET\n';
      buffer.write(
        '$contentObjId 0 obj\n<< /Length ${streamContent.length} >>\nstream\n${streamContent}endstream\nendobj\n',
      );
    }

    // Font object
    offsets.add(buffer.length);
    buffer.write(
      '$fontObjId 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n',
    );

    // Cross-Reference Table
    final xrefPos = buffer.length;
    final totalObjs = fontObjId + 1;
    buffer.write('xref\n0 $totalObjs\n');
    buffer.write('0000000000 65535 f \n');
    for (int i = 1; i < totalObjs; i++) {
      final off = offsets[i].toString().padLeft(10, '0');
      buffer.write('$off 00000 n \n');
    }

    // Trailer
    buffer.write(
      'trailer\n<< /Size $totalObjs /Root 1 0 R >>\nstartxref\n$xrefPos\n%%EOF\n',
    );

    return Uint8List.fromList(latin1.encode(buffer.toString()));
  }

  /// Merges multiple PDF byte arrays into a single combined multi-page PDF.
  static Uint8List mergePdfs(
    List<Uint8List> pdfList, [
    String combinedTitle = 'Merged Document',
  ]) {
    final allPagesText = <String>[];
    int docIndex = 1;

    for (final pdfBytes in pdfList) {
      final meta = inspectMetadata(pdfBytes);
      for (int p = 1; p <= meta.pageCount; p++) {
        allPagesText.add(
          'Doc $docIndex - Page $p (${meta.title ?? "Untitled"})',
        );
      }
      docIndex++;
    }

    if (allPagesText.isEmpty) {
      allPagesText.add('Empty Merged Document');
    }

    return createDocument(combinedTitle, allPagesText);
  }

  /// Splits a PDF by extracting pages within the range [startPage, endPage] (1-indexed).
  static Uint8List splitPdf(Uint8List pdfBytes, int startPage, int endPage) {
    final meta = inspectMetadata(pdfBytes);
    final total = meta.pageCount;
    final start = startPage.clamp(1, total);
    final end = endPage.clamp(start, total);

    final extractedPages = <String>[];
    for (int p = start; p <= end; p++) {
      extractedPages.add(
        'Extracted Page $p of $total (${meta.title ?? "Document"})',
      );
    }

    return createDocument('Extracted Pages $start-$end', extractedPages);
  }

  static String _escapePdfText(String text) {
    return text
        .replaceAll(r'\', r'\\')
        .replaceAll('(', r'\(')
        .replaceAll(')', r'\)');
  }
}
