namespace AiKnowledgeAssistant.Infrastructure.OCR;

public interface IPdfPageOcr
{
    string Recognize(string pdfPath, int physicalPageNumber);
}
