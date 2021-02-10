using System;
using System.Collections.Generic;
using System.Text;
using System.Web;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Data.ViewModels.Search;
using Framework;
using Framework.Data;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Storage;
using Framework.Strings;
using Lucene.Net.Analysis.Lt;
using Lucene.Net.QueryParsers;
using MongoDB.Bson;
using Services.Infrastructure;
using Services.Session;

namespace Services.ModelServices
{
    public class SearchService : IService
    {
        private readonly Func<INoSqlSession> noSqlSessionFactory;
        private Lucene.Net.Analysis.Analyzer analyzer;
        private Lucene.Net.Search.Searcher searcher = null;
        private readonly ILogger logger;
        private Lucene.Net.QueryParsers.QueryParser parser;
        private Lucene.Net.Search.Query query = null;

        public UserInfo CurrentUser { get { return MembershipSession.GetUser(); } }

        public SearchService(
            Func<INoSqlSession> noSqlSessionFactory, ILogger logger)
        {
            this.noSqlSessionFactory = noSqlSessionFactory;
            this.logger = logger;
            analyzer = new LithuanianAnalyzer();
            parser = new QueryParser(Lucene.Net.Util.Version.LUCENE_29, "text", analyzer);
            parser.SetMultiTermRewriteMethod(Lucene.Net.Search.MultiTermQuery.SCORING_BOOLEAN_QUERY_REWRITE);
        }

        public SearchModel Search(string searchText)
        {
            var result = new SearchModel();

            if (string.IsNullOrEmpty(searchText))
            {
                result.Message = "Įveskite paieškos užklausą.";
                return result;
            }

            var stemmedSearchText = new LithuanianStemmer().Stem(searchText.Trim());

            if (string.IsNullOrEmpty(stemmedSearchText))
            {
                result.Message = "Įveskite paieškos užklausą.";
                return result;
            }

            Lucene.Net.Search.Hits hits = null;
            try
            {
                if (char.IsLetter(stemmedSearchText[stemmedSearchText.Length - 1]))
                {
                    stemmedSearchText += "*";
                }

                query = parser.Parse(stemmedSearchText);

                if (searcher == null)
                {
                    searcher = new Lucene.Net.Search.IndexSearcher(CustomAppSettings.SearchIndexFolder);
                }

                hits = searcher.Search(query);

            }
            catch (Exception e)
            {
                result.Message = "Paieška nepavyko. Pataisykite užklausą. Klaidos pranešimas: " + e.Message;
                return result;
            }

            Lucene.Net.Highlight.Formatter formatter = new Lucene.Net.Highlight.SimpleHTMLFormatter(
                "<span class=\"highlightResult\">",
                "</span>");

            var fragmenter = new Lucene.Net.Highlight.SimpleFragmenter(100);
            var scorer = new Lucene.Net.Highlight.QueryScorer(searcher.Rewrite(query));
            var highlighter = new Lucene.Net.Highlight.Highlighter(formatter, scorer);
            highlighter.SetTextFragmenter(fragmenter);

            Dictionary<string, int> dict_already_seen_ids = new Dictionary<string, int>();

            var list = new List<SearchIndexModel>();

            // insert the search results into a temp table which we will join with what's in the database
            for (int i = 0; i < hits.Length(); i++)
            {
                if (dict_already_seen_ids.Count < 100)
                {
                    Lucene.Net.Documents.Document doc = hits.Doc(i);
                    string id = doc.Get("id");
                    if (!dict_already_seen_ids.ContainsKey(id))
                    {
                        dict_already_seen_ids[id] = 1;
                        var model = new SearchIndexModel();
                        model.Id = id;
                        model.Score =hits.Score(i);
                        model.Subject = doc.Get("subject");
                        model.Type = (EntryTypes) Enum.Parse(typeof (EntryTypes), doc.Get("type"));
                        
                        string raw_text = HttpUtility.HtmlEncode(doc.Get("raw_text"));
                        //string raw_text = doc.Get("raw_text");

                        Lucene.Net.Analysis.TokenStream stream = analyzer.TokenStream("text",
                                                                                      new System.IO.StringReader(
                                                                                          raw_text));
                        string highlighted_text = highlighter.GetBestFragments(stream, raw_text, 3, "...").Replace("'",
                                                                                                                   "''");


                        if (highlighted_text == "") // someties the highlighter fails to emit text...
                        {
                            highlighted_text = raw_text.Replace("'", "''");
                        }
                        if (highlighted_text.Length > 3000)
                        {
                            highlighted_text = highlighted_text.Substring(0, 3000);
                        }
                     
                        model.HighlightedText = highlighted_text;

                        list.Add(model);
                    }
                }
                else
                {
                    break;
                }
            }

            result.List = list;
            result.SearchPhrase = searchText;
            if(list.Count == 0)
            {
                result.Message = string.Format("Įrašų pagal užklausą '{0}' nerasta. Patikslinkite paieškos duomenis.", searchText);
            }

            return result;
        }

        public void BuildIndex()
        {
            Lucene.Net.Index.IndexWriter writer = new Lucene.Net.Index.IndexWriter(CustomAppSettings.SearchIndexFolder, analyzer, true);

            using(var noSqlSession = noSqlSessionFactory())
            {
                foreach(var idea in noSqlSession.GetAll<Idea>())
                {
                    writer.AddDocument(CreateDoc(idea.Id, CreateSearchText(idea), idea.Subject, EntryTypes.Idea));
                }

                foreach (var issue in noSqlSession.GetAll<Issue>())
                {
                    writer.AddDocument(CreateDoc(issue.Id, CreateSearchText(issue), issue.Subject, EntryTypes.Issue));
                }

                foreach (var user in noSqlSession.GetAll<User>())
                {
                    writer.AddDocument(CreateDoc(user.Id, user.FullName, user.FullName, EntryTypes.User));
                }

                foreach (var org in noSqlSession.GetAll<Organization>())
                {
                    writer.AddDocument(CreateDoc(org.Id, org.Name, org.Name, EntryTypes.Organization));
                }

                foreach (var prob in noSqlSession.GetAll<Problem>())
                {
                    writer.AddDocument(CreateDoc(prob.Id, CreateSearchText(prob), prob.Text.LimitLength(100), EntryTypes.Problem));
                }
            }

            writer.Optimize();
            writer.Close();
        }

        private string CreateSearchText(Idea idea)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(idea.Subject + " ");
            foreach(var version in idea.SummaryWiki.Versions)
            {
                sb.Append(version.Subject + " ");
                sb.Append(version.Text + " ");
            }

            foreach(var comment in idea.Comments)
            {
                sb.Append(comment.Text + " ");
                foreach (var cComment in comment.Comments)
                {
                    sb.Append(cComment.Text + " ");
                }
            }

            return sb.ToString();
        }

        private string CreateSearchText(Issue issue)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(issue.Subject + " ");
            foreach (var version in issue.SummaryWiki.Versions)
            {
                sb.Append(version.Subject + " ");
                sb.Append(version.Text + " ");
            }

            foreach (var comment in issue.Comments)
            {
                sb.Append(comment.Text + " ");
                foreach (var cComment in comment.Comments)
                {
                    sb.Append(cComment.Text + " ");
                }
            }

            return sb.ToString();
        }

        private string CreateSearchText(Problem issue)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(issue.Text + " ");

            foreach (var comment in issue.Comments)
            {
                sb.Append(comment.Text + " ");
                foreach (var cComment in comment.Comments)
                {
                    sb.Append(cComment.Text + " ");
                }
            }

            return sb.ToString();
        }

        private Lucene.Net.Documents.Document CreateDoc(MongoObjectId id, string text, string subject, EntryTypes type)
        {
            Lucene.Net.Documents.Document doc = new Lucene.Net.Documents.Document();

            doc.Add(new Lucene.Net.Documents.Field(
                "text",
                new System.IO.StringReader(text.StripHtml())));

            doc.Add(new Lucene.Net.Documents.Field(
                "id",
                id.ToString(),
                Lucene.Net.Documents.Field.Store.YES,
                Lucene.Net.Documents.Field.Index.UN_TOKENIZED));

            doc.Add(new Lucene.Net.Documents.Field(
                "subject",
                subject,
                Lucene.Net.Documents.Field.Store.YES,
                Lucene.Net.Documents.Field.Index.UN_TOKENIZED));

            doc.Add(new Lucene.Net.Documents.Field(
                "type",
                type.ToString(),
                Lucene.Net.Documents.Field.Store.YES,
                Lucene.Net.Documents.Field.Index.UN_TOKENIZED));

            // For the highlighter, store the raw text
            doc.Add(new Lucene.Net.Documents.Field(
                "raw_text",
                text.StripHtml(),
                Lucene.Net.Documents.Field.Store.YES,
                Lucene.Net.Documents.Field.Index.UN_TOKENIZED));

            return doc;
        }

        public void DeleteIndex(MongoObjectId id)
        {
            Lucene.Net.Index.IndexModifier modifier = null;

            try
            {
                if (searcher != null)
                {
                    try
                    {
                        searcher.Close();
                    }
                    catch (Exception e)
                    {
                        logger.Error("Exception closing lucene searcher:" + e.Message, e);
                        throw;
                    }
                    searcher = null;
                }

                modifier =
                    new Lucene.Net.Index.IndexModifier(CustomAppSettings.SearchIndexFolder, analyzer, false);

                // same as build, but uses "modifier" instead of write.
                // uses additional "where" clause for bugid

                modifier.DeleteDocuments(new Lucene.Net.Index.Term("id", id.ToString()));
            }
            catch (Exception e)
            {
                logger.Error("Exception closing lucene searcher:" + e.Message, e);
            }
            finally
            {
                if (modifier != null)
                {
                    modifier.Flush();
                    modifier.Close();
                }
            }
        }

        public void UpdateIndex(MongoObjectId id, EntryTypes type)
        {
            if(id == null)
            {
                return;
            }

            Lucene.Net.Index.IndexModifier modifier = null;
            try
            {
                if (searcher != null)
                {
                    try
                    {
                        searcher.Close();
                    }
                    catch (Exception e)
                    {
                        logger.Error("Exception closing lucene searcher:" + e.Message, e);
                        throw;
                    }
                    searcher = null;
                }

                modifier =
                    new Lucene.Net.Index.IndexModifier(CustomAppSettings.SearchIndexFolder, analyzer, false);

                // same as build, but uses "modifier" instead of write.
                // uses additional "where" clause for bugid

                modifier.DeleteDocuments(new Lucene.Net.Index.Term("id", id.ToString()));

                using (var noSqlSession = noSqlSessionFactory())
                {
                    switch (type)
                    {
                        case EntryTypes.Idea:
                            var idea = noSqlSession.GetById<Idea>(id);
                            if (idea != null)
                            {
                                modifier.AddDocument(CreateDoc(id, CreateSearchText(idea), idea.Subject, type));
                            }
                            break;
                        case EntryTypes.Issue:
                            var issue = noSqlSession.GetById<Issue>(id);
                            if (issue != null)
                            {
                                modifier.AddDocument(CreateDoc(id, CreateSearchText(issue), issue.Subject, type));
                            }
                            break;
                        case EntryTypes.User:
                            var user = noSqlSession.GetById<User>(id);
                            if (user != null)
                            {
                                modifier.AddDocument(CreateDoc(id, user.FullName, user.FullName, type));
                            }
                            break;
                        case EntryTypes.Organization:
                            var org = noSqlSession.GetById<Organization>(id);
                            if (org != null)
                            {
                                modifier.AddDocument(CreateDoc(id, org.Name, org.Name, type));
                            }
                            break;
                        case EntryTypes.Problem:
                            var prob = noSqlSession.GetById<Problem>(id);
                            if (prob != null)
                            {
                                modifier.AddDocument(CreateDoc(id, CreateSearchText(prob), prob.Text.LimitLength(100), type));
                            }
                            break;
                        default:
                            break;
                    }
                }

                
            }
            catch (Exception e)
            {
                logger.Error("exception updating Lucene index: " + e.Message, e);
            }
            finally
            {
                try
                {
                    if (modifier != null)
                    {
                        modifier.Flush();
                        modifier.Close();
                    }
                }
                catch (Exception e)
                {
                    logger.Error("exception updating Lucene index: " + e.Message, e);
                }
            }
        }
    }
}
