using System;
using System.IO;
using Data.Enums;
using Data.ViewModels.Voting;
using iTextSharp.text;
using iTextSharp.text.html.simpleparser;
using iTextSharp.text.pdf;

namespace Services.Documents
{
    public class IssueGenerator : DocumentGenerator
    {
        public byte[] Generate(IssueDocumentModel model)
        {
            using (var memStream = new MemoryStream())
            {
                using (var doc = new Document())
                {
                    //doc.Open();

                    // Initialize the PDF document
                    iTextSharp.text.pdf.PdfWriter writer = iTextSharp.text.pdf.PdfWriter.GetInstance(doc, memStream);

                    // Set the margins and page size
                    this.SetStandardPageSize(doc);

                    // Add metadata to the document.  This information is visible when viewing the 
                    // document properities within Adobe Reader.
                    var subject = "Balsavimas \"" + model.Subject + "\"";
                    doc.AddTitle(subject);
                    doc.AddHeader("title", subject);
                    doc.AddHeader("author", "Lietuva 2.0");
                    doc.AddCreator("Lietuva 2.0");

                    // Add Xmp metadata to the document.
                    this.CreateXmpMetadata(writer, subject);

                    // Open the document for writing content
                    doc.Open();

                    // Write page content.  Note the use of fonts and alignment attributes.
                    this.AddParagraph(doc, iTextSharp.text.Element.ALIGN_CENTER, _largeFont, new iTextSharp.text.Chunk("\n\n"));
                    this.AddParagraph(doc, iTextSharp.text.Element.ALIGN_CENTER, _largeFont, new Chunk(subject + "\n\n"));
                    
                    this.AddHtml(doc, model.Summary);
                    if (!string.IsNullOrEmpty(model.OfficialVoteDesciprtion))
                    {
                        this.AddParagraph(doc, iTextSharp.text.Element.ALIGN_LEFT, _standardFont, new Chunk("\n\n"));
                        this.AddHtml(doc, model.OfficialVoteDesciprtion);
                    }

                    this.AddParagraph(doc, iTextSharp.text.Element.ALIGN_LEFT, _standardFont, new Chunk("\n\n"));
                    this.AddParagraph(doc, iTextSharp.text.Element.ALIGN_LEFT, _standardFont, new Chunk("UŽ: " + model.SupportingVotesCount));
                    this.AddParagraph(doc, iTextSharp.text.Element.ALIGN_LEFT, _standardFont, new Chunk("PRIEŠ: " + model.NonSupportingVotesCount));
                    if (model.NeutralVotesCount > 0)
                    {
                        this.AddParagraph(doc, iTextSharp.text.Element.ALIGN_LEFT, _standardFont,
                                          new Chunk("SUSILAIKO: " + model.NeutralVotesCount));
                    }

                    if (model.SupportingUsersCount != model.SupportingVotesCount ||
                        model.NonSupportingUsersCount != model.NonSupportingVotesCount ||
                        model.NeutralUsersCount != model.NeutralVotesCount)
                    {
                        this.AddParagraph(doc, iTextSharp.text.Element.ALIGN_LEFT, _standardFont, new Chunk("\n\n"));
                        this.AddParagraph(doc, iTextSharp.text.Element.ALIGN_LEFT, _standardFont,
                            new Chunk("Palaikančių narių: " + model.SupportingUsersCount));
                        this.AddParagraph(doc, iTextSharp.text.Element.ALIGN_LEFT, _standardFont,
                            new Chunk("Nepalaikančių narių: " + model.NonSupportingUsersCount));
                        if (model.NeutralUsersCount > 0)
                        {
                            this.AddParagraph(doc, iTextSharp.text.Element.ALIGN_LEFT, _standardFont,
                                new Chunk("Susilaikančių narių: " + model.NeutralUsersCount));
                        }
                    }

                    this.AddParagraph(doc, iTextSharp.text.Element.ALIGN_CENTER, _largeFont, new Chunk("\n\n\n"));

                    PdfPTable table = new PdfPTable(6);
                    table.WidthPercentage = 100;
                    table.AddCell(new Phrase("Vardas", _boldFont));
                    table.AddCell(new Phrase("Pavardė", _boldFont));
                    table.AddCell(new Phrase("Data", _boldFont));
                    table.AddCell(new Phrase("Būdas", _boldFont));
                    table.AddCell(new Phrase("Balsas", _boldFont));
                    table.AddCell(new Phrase("Galioja", _boldFont));
                    foreach (var user in model.Users)
                    {
                        table.AddCell(new Phrase(user.FirstName, _standardFont));
                        table.AddCell(new Phrase(user.LastName, _standardFont));
                        table.AddCell(new Phrase(user.Date.ToString(), _standardFont));
                        table.AddCell(new Phrase(user.Source, _standardFont));
                        table.AddCell(new Phrase(user.Vote == ForAgainst.For ? "Už" : user.Vote == ForAgainst.Against ? "Prieš" : "Susilaiko", _standardFont));
                        table.AddCell(new Phrase(user.IsValid ? "Taip" : "Ne", _standardFont));
                    }

                    doc.Add(table);

                    doc.Close();

                    return memStream.ToArray();
                }
            }

            return null;
        }
    }
}
