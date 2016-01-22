using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using NuGet.Indexing;
using NuGet.Protocol.Core.Types;
using System.Collections.Generic;
using System.Linq;

namespace NuGet.Protocol.VisualStudio.Services
{
    public class LuceneSearchResultsIndexer : ISearchResultsIndexer
    {
        private readonly Directory _directory;
        private readonly Analyzer _packageAnalyzer;

        public LuceneSearchResultsIndexer()
        {
            _directory = new RAMDirectory();
            _packageAnalyzer = new PackageAnalyzer();
        }

        public void AddToIndex(IEnumerable<PackageSearchMetadata> entries)
        {
            using (var writer = new IndexWriter(_directory, _packageAnalyzer, IndexWriter.MaxFieldLength.UNLIMITED))
            {
                foreach (var document in entries.Select(CreateDocument))
                {
                    writer.AddDocument(document);
                }
                writer.Commit();
            }
        }

        private static Document CreateDocument(PackageSearchMetadata item)
        {
            var doc = new Document();
            doc.Add(new Field("Id", item.Identity.Id, Field.Store.YES, Field.Index.ANALYZED));
            doc.Add(new Field("Version", item.Identity.Version.ToString(), Field.Store.NO, Field.Index.ANALYZED));
            doc.Add(new Field("Summary", item.Summary ?? string.Empty, Field.Store.NO, Field.Index.ANALYZED));
            doc.Add(new Field("Description", item.Description ?? string.Empty, Field.Store.NO, Field.Index.ANALYZED));
            doc.Add(new Field("Title", item.Title ?? string.Empty, Field.Store.NO, Field.Index.ANALYZED));
            item.Tags
                .Select(tag => new Field("Tags", tag, Field.Store.NO, Field.Index.ANALYZED))
                .ToList()
                .ForEach(doc.Add);
            return doc;
        }

        public IDictionary<string, int> Search(string queryString)
        {
            var searcher = new IndexSearcher(_directory);
            var query = NuGetQuery.MakeQuery(queryString);
            var topDocs = searcher.Search(query, 100);

            var rankingByRelevance = topDocs.ScoreDocs
                .Select(d => searcher.Doc(d.Doc))
                .Zip(Enumerable.Range(0, topDocs.ScoreDocs.Length), (doc, rank) => new { doc, rank })
                .ToDictionary(x => x.doc.Get("Id"), x => x.rank);

            return rankingByRelevance;
        }
    }
}
