using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.Snowball;
using Lucene.Net.Analysis.Cn;
using Lucene.Net.Analysis.CJK;
using Lucene.Net.Analysis.Cz;
using Lucene.Net.Documents;
using Lucene.Net.QueryParsers;
using Lucene.Net.Index;
using Lucene.Net.Search;
using RAMDirectory = Lucene.Net.Store.RAMDirectory;
using FSDirectory = Lucene.Net.Store.FSDirectory;

namespace BzReader
{
    public class Indexer
    {
        private const int MAX_RAMDIRECTORY_DOCS = 500000;
        /// <summary>
        /// The maximum hits to return per query
        /// </summary>
        public const int MAX_SEARCH_HITS = 100;
        /// <summary>
        /// The maximum number of decoded blocks to keep in memory. A single block is roughly 1 M characters = 2 Mb
        /// </summary>
        private const int MAX_CACHED_BLOCKS_NO = 30;
        /// <summary>
        /// The maximum number of blocks we would be prepared to process in bz2 file.
        /// 200000 blocks = 160 Gb uncompressed, hope this is enough
        /// </summary>
        private const int MAX_BLOCKS_NO = 200000;
        /// <summary>
        /// UTF8 decoder which would throw on unknown character
        /// </summary>
        private Decoder utf8 = new UTF8Encoding(true, false).GetDecoder();
        /// <summary>
        /// The index searcher
        /// </summary>
        private IndexSearcher searcher;
        /// <summary>
        /// Whether the index exists at the specified location
        /// </summary>
        private bool indexExists;
        /// <summary>
        /// The path to the dump file
        /// </summary>
        private string filePath;
        /// <summary>
        /// The path to the index
        /// </summary>
        private string indexPath;
        /// <summary>
        /// Occurs when the indexing is happening and the progress changes
        /// </summary>
        public event ProgressChangedEventHandler ProgressChanged;
        /// <summary>
        /// The total number of blocks in the file
        /// </summary>
        private long totalBlocks = MAX_BLOCKS_NO;
        /// <summary>
        /// The buffer for the beginnings of the blocks
        /// </summary>
        private long[] beginnings = new long[MAX_BLOCKS_NO];
        /// <summary>
        /// The buffer for the ends of the blocks
        /// </summary>
        private long[] ends = new long[MAX_BLOCKS_NO];
        /// <summary>
        /// The buffer for the current block
        /// </summary>
        private byte[] blockBuf;
        /// <summary>
        /// The character buffer for the current block
        /// </summary>
        private char[] charBuf;
        /// <summary>
        /// The indexing thread
        /// </summary>
        private Thread indexingThread;
        /// <summary>
        /// The bzip2-watching thread
        /// </summary>
        private Thread bzip2WatchThread;
        /// <summary>
        /// Whether the indexing process should be aborted
        /// </summary>
        private bool abortIndexing;
        /// <summary>
        /// Percent done for bzip2 block location progress
        /// </summary>
        private int bz2_blocks_pct_done;
        /// <summary>
        /// Total bz2 file size, for progress calculation
        /// </summary>
        private long bz2_filesize;
        /// <summary>
        /// The cache of decoded blocks from file
        /// </summary>
        private Dictionary<long, byte[]> blocksCache = new Dictionary<long, byte[]>();
        /// <summary>
        /// The analyzer to use for indexing and query tokenizing
        /// </summary>
        private Analyzer textAnalyzer;
        /// <summary>
        /// The query parser for the searches in this index
        /// </summary>
        private QueryParser queryParser;
        /// <summary>
        /// Whether to use multiple threads while indexing the documents
        /// </summary>
        private bool multithreadedIndexing = false;
        /// <summary>
        /// start time for operations
        /// </summary>
        private DateTime startTime;
        /// <summary>
        /// elapsed time for operations
        /// </summary>
        private TimeSpan elapsed;

        /// <summary>
        /// Initializes a new instance of the <see cref="Indexer"/> class.
        /// </summary>
        /// <param name="path">The path to the .xml.bz2 dump of wikipedia</param>
        public Indexer(string path)
        {
            filePath = path;

            indexPath = Path.ChangeExtension(path, ".idx");

            if (Directory.Exists(indexPath) &&
                IndexReader.IndexExists(indexPath))
            {
                indexExists = true;
            }

            if (indexExists)
            {
                searcher = new IndexSearcher(indexPath);
            }

            textAnalyzer = GuessAnalyzer(filePath);

            queryParser = new QueryParser("title", textAnalyzer);
            
            queryParser.SetDefaultOperator(QueryParser.Operator.AND);

            abortIndexing = false;

            multithreadedIndexing = (Environment.ProcessorCount > 1);
        }

        #region Indexing methods

        /// <summary>
        /// Gets a value indicating whether index exists.
        /// </summary>
        /// <value><c>true</c> if index exists; otherwise, <c>false</c>.</value>
        public bool IndexExists
        {
            get
            {
                return indexExists;
            }
        }

        /// <summary>
        /// The index writer which is used for indexing
        /// </summary>
        private IndexWriter indexer;
        private IndexWriter memoryIndexer;

        /// <summary>
        ///  watch the bzip2 block locator and report progress
        /// </summary>
        private void BzipBlockLocatorProgressMonitor()
        {
            while (true) // we expect to be aborted externally
            {
                Thread.Sleep(500);
                ReportProgress(bz2_blocks_pct_done, IndexingProgress.State.Running, String.Empty);
            }
        }

        /// <summary>
        /// Creates the index for the bz2 file on a separate thread.
        /// </summary>
        private void CreateIndexAsync()
        {
            bool failed = false;
            startTime = DateTime.Now;

            try
            {
                // Close any searchers

                if (searcher != null)
                {
                    searcher.Close();

                    searcher = null;
                }

                indexExists = false;

                // Create the index writer

                indexer = new IndexWriter(indexPath, textAnalyzer, true);
                memoryIndexer = new IndexWriter(new RAMDirectory(), textAnalyzer, true);

                memoryIndexer.SetMaxBufferedDocs(1000);
                memoryIndexer.SetMergeFactor(100);

                indexer.SetMaxBufferedDocs(1000);
                indexer.SetMergeFactor(100);

                // Locate the bzip2 blocks in the file

                LocateBlocks();

                // Two times more than the first block but not less than 100 bytes

                long bufSize = ((ends[0] - beginnings[0]) / 8) * 2 + 100;

                // Buffers for the current and next block

                blockBuf = new byte[bufSize];
                charBuf = new char[bufSize];

                // Whether there was a Wiki topic carryover from current block to the next one

                char[] charCarryOver = new char[0];

                // The length of the currently loaded data

                long loadedLength = 0;

                StringBuilder sb = new StringBuilder();

                // Starting indexing

                startTime = DateTime.Now;
                elapsed = new TimeSpan(0);
                ReportProgress(0, IndexingProgress.State.Running, Properties.Resources.ProgressIndexing);
                for (long i = 0; i < totalBlocks && !abortIndexing; i++)
                {
                    ReportProgress((int)((double)(i * 100) / (double)totalBlocks), IndexingProgress.State.Running, String.Empty);

                    #region Indexing logic

                    loadedLength = LoadBlock(beginnings[i], ends[i], ref blockBuf);

                    if (charBuf.Length < blockBuf.Length)
                    {
                        charBuf = new char[blockBuf.Length];
                    }

                    int bytesUsed = 0;
                    int charsUsed = 0;
                    bool completed = false;

                    // Convert the text to UTF8

                    utf8.Convert(blockBuf, 0, (int)loadedLength, charBuf, 0, charBuf.Length, i == totalBlocks - 1, out bytesUsed, out charsUsed, out completed);

                    if (!completed)
                    {
                        throw new Exception(Properties.Resources.UTFDecoderError);
                    }

                    // Construct a current string

                    sb.Length = 0;

                    if (charCarryOver.Length > 0)
                    {
                        sb.Append(charCarryOver);
                    }

                    sb.Append(charBuf, 0, charsUsed);

                    int carryOverLength = charCarryOver.Length;

                    int charsMatched = IndexString(sb.ToString(), beginnings[i], ends[i], carryOverLength, i == totalBlocks - 1);

                    // There's a Wiki topic carryover, let's store the characters which need to be carried over 

                    if (charsMatched > 0)
                    {
                        charCarryOver = new char[charsMatched];

                        sb.CopyTo(charsUsed + carryOverLength - charsMatched, charCarryOver, 0, charsMatched);
                    }
                    else
                    {
                        charCarryOver = new char[0];
                    }

                    #endregion
                }

                // Wait till all the threads finish
                while (activeThreads != 0)
                {
                    ReportProgress(0, IndexingProgress.State.Running, String.Format(Properties.Resources.WaitingForTokenizers, activeThreads));

                    Thread.Sleep(TimeSpan.FromSeconds(5));
                }
                ReportProgress(0, IndexingProgress.State.Running, Properties.Resources.FlushingDocumentsToDisk);

                Lucene.Net.Store.Directory dir = memoryIndexer.GetDirectory();

                memoryIndexer.Close();

                indexer.AddIndexes(new Lucene.Net.Store.Directory[] { dir });

                memoryIndexer = null;
                ReportProgress(0, IndexingProgress.State.Running, Properties.Resources.OptimizingIndex);

                indexer.Optimize();

                indexExists = true;
            }
            catch (Exception ex)
            {
                ReportProgress(0, IndexingProgress.State.Failure, ex.ToString());

                failed = true;
            }

            // Try to release some memory

            if (indexer != null)
            {
                indexer.Close();

                indexer = null;
            }

            if (failed ||
                abortIndexing)
            {
                Directory.Delete(indexPath, true);

                indexExists = false;
            }
            else
            {
                if (indexExists)
                {
                    searcher = new IndexSearcher(indexPath);
                }
            }
            ReportProgress(0, IndexingProgress.State.Finished, String.Empty);
        }

        /// <summary>
        /// Store the offsets of the previous block in case of the Wiki topic carryover
        /// </summary>
        private long previousBlockBeginning = -1;
        private long previousBlockEnd = -1;
        private int activeThreads = 0;

        /// <summary>
        /// Indexes the provided string
        /// </summary>
        /// <param name="currentText">The string to index</param>
        /// <param name="beginning">The beginning offset of the block</param>
        /// <param name="end">The end offset of the block</param>
        /// <param name="charCarryOver">Whether there was a Wiki topic carryover from previous block</param>
        /// <param name="lastBlock">True if this is the last block</param>
        /// <returns>The number of characters in the end of the string that match the header entry</returns>
        private int IndexString(string currentText, long beginning, long end, int charCarryOver, bool lastBlock)
        {
            bool firstRun = true;
            
            int topicStart = currentText.IndexOf("<title>", StringComparison.InvariantCultureIgnoreCase);
            
            int titleEnd, idStart, idEnd, topicEnd = -1;

            string title = String.Empty;
            long id = -1;

            while (topicStart >= 0 &&
                !abortIndexing)
            {
                titleEnd = -1;
                idStart = -1;
                idEnd = -1;
                topicEnd = -1;

                titleEnd = currentText.IndexOf("</title>", topicStart, StringComparison.InvariantCultureIgnoreCase);

                if (titleEnd < 0)
                {
                    break;
                }

                title = currentText.Substring(topicStart + "<title>".Length, titleEnd - topicStart - "<title>".Length);

                idStart = currentText.IndexOf("<id>", titleEnd, StringComparison.InvariantCultureIgnoreCase);

                if (idStart < 0)
                {
                    break;
                }

                idEnd = currentText.IndexOf("</id>", idStart, StringComparison.InvariantCultureIgnoreCase);

                if (idEnd < 0)
                {
                    break;
                }

                id = Convert.ToInt64(currentText.Substring(idStart + "<id>".Length, idEnd - idStart - "<id>".Length));

                topicEnd = currentText.IndexOf("</text>", idEnd, StringComparison.InvariantCultureIgnoreCase);

                if (topicEnd < 0)
                {
                    break;
                }

                // Start creating the object for the tokenizing ThreadPool thread

                long[] begins = new long[1];
                long[] ends = new long[1];

                // Was there a carryover?

                if (firstRun)
                {
                    // Did the <title> happen in the carryover area?

                    if (charCarryOver > 0 &&
                        topicStart < charCarryOver)
                    {
                        if (previousBlockBeginning > -1 &&
                            previousBlockEnd > -1)
                        {
                            begins = new long[2];
                            ends = new long[2];

                            begins[1] = previousBlockBeginning;
                            ends[1] = previousBlockEnd;
                        }
                        else
                        {
                            throw new Exception(Properties.Resources.CarryoverNoPrevBlock);
                        }
                    }
                }

                begins[0] = beginning;
                ends[0] = end;

                PageInfo pi = new PageInfo(id, title, begins, ends);

                Interlocked.Increment(ref activeThreads);

                if (multithreadedIndexing)
                {
                    ThreadPool.QueueUserWorkItem(TokenizeAndAdd, pi);
                }
                else
                {
                    TokenizeAndAdd(pi);
                }

                // Store the last successful title start position

                int nextTopicStart = currentText.IndexOf("<title>", topicStart + 1, StringComparison.InvariantCultureIgnoreCase);

                if (nextTopicStart >= 0)
                {
                    topicStart = nextTopicStart;
                }
                else
                {
                    break;
                }

                firstRun = false;
            }

            // Now calculate how many characters we need to save for next block

            int charsToSave = 0;

            if (topicStart == -1)
            {
                if (!lastBlock)
                {
                    throw new Exception(Properties.Resources.NoTopicsInBlock);
                }
            }
            else
            {
                if (!lastBlock)
                {
                    if (topicEnd == -1)
                    {
                        charsToSave = currentText.Length - topicStart;
                    }
                    else
                    {
                        if (topicStart < topicEnd)
                        {
                            charsToSave = currentText.Length - topicEnd - "</text>".Length;
                        }
                        else
                        {
                            charsToSave = currentText.Length - topicStart;
                        }
                    }
                }
            }

            // Flush the memory indexer to disk from time to time

            if (memoryIndexer.DocCount() >= MAX_RAMDIRECTORY_DOCS)
            {
                ReportProgress(0, IndexingProgress.State.Running, Properties.Resources.FlushingDocumentsToDisk);

                while (activeThreads != 0)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(5));
                }

                Lucene.Net.Store.Directory dir = memoryIndexer.GetDirectory();

                memoryIndexer.Close();
                
                indexer.AddIndexes(new Lucene.Net.Store.Directory[]{ dir });

                memoryIndexer = new IndexWriter(new RAMDirectory(), textAnalyzer, true);
            }

            previousBlockBeginning = beginning;
            previousBlockEnd = end;

            return charsToSave;
        }

        /// <summary>
        /// Tokenizes and adds the specified PageInfo object to Lucene index
        /// </summary>
        /// <param name="state">PageInfo object</param>
        private void TokenizeAndAdd(object state)
        {
            PageInfo pi = (PageInfo)state;

            Document d = new Document();

            // Store title

            d.Add(new Field("title", pi.Name, Field.Store.YES, Field.Index.TOKENIZED));

            // Current block offsets

            d.Add(new Field("beginning", BitConverter.GetBytes(pi.Beginnings[0]), Field.Store.YES));
            d.Add(new Field("end", BitConverter.GetBytes(pi.Ends[0]), Field.Store.YES));

            if (pi.Beginnings.Length == 2)
            {
                // Previous block offsets

                d.Add(new Field("beginning", BitConverter.GetBytes(pi.Beginnings[1]), Field.Store.YES));
                d.Add(new Field("end", BitConverter.GetBytes(pi.Ends[1]), Field.Store.YES));
            }

            // Topic ID

            d.Add(new Field("topicid", pi.TopicId.ToString(), Field.Store.YES, Field.Index.UN_TOKENIZED));

            memoryIndexer.AddDocument(d);

            Interlocked.Decrement(ref activeThreads);
        }

        #endregion

        #region Block methods

        /// <summary>
        /// Class-level variable to do the allocation just once
        /// </summary>
        private byte[] decompressionBuf;

        /// <summary>
        /// Loads the bzip2 block from the file
        /// </summary>
        /// <param name="beginning">The beginning of the block, in bits.</param>
        /// <param name="end">The end of the block, in bits.</param>
        /// <param name="buf">The buffer to load into.</param>
        /// <returns>The length of the loaded data, in bytes</returns>
        private long LoadBlock(long beginning, long end, ref byte[] buf)
        {
            long bufSize = buf.LongLength;

            bzip2.StatusCode status = bzip2.BZ2_bzLoadBlock(filePath, beginning, end, buf, ref bufSize);

            if (status != bzip2.StatusCode.BZ_OK)
            {
                throw new Exception(String.Format(Properties.Resources.FailedLoadingBlock, filePath, beginning, status));
            }

            // Just some initial value, we will reallocate the buffer as needed

            if (decompressionBuf == null)
            {
                decompressionBuf = new byte[buf.Length * 4];
            }

            int intBufSize = (int)bufSize;
            int intDecompSize = decompressionBuf.Length;

            status = bzip2.BZ2_bzDecompress(buf, intBufSize, decompressionBuf, ref intDecompSize);

            // Limit a single uncompressed block size to 32 Mb

            while (status == bzip2.StatusCode.BZ_OUTBUFF_FULL &&
                decompressionBuf.Length < 32000000)
            {
                decompressionBuf = new byte[decompressionBuf.Length * 2];

                intDecompSize = decompressionBuf.Length;

                status = bzip2.BZ2_bzDecompress(buf, intBufSize, decompressionBuf, ref intDecompSize);
            }

            if (decompressionBuf.Length > 32000000)
            {
                throw new Exception(String.Format(Properties.Resources.FailedUncompressingMemory, beginning));
            }

            if (status != bzip2.StatusCode.BZ_OK)
            {
                throw new Exception(String.Format(Properties.Resources.FailedUncompressingStatus, beginning, status));
            }

            // Exchange the raw buffer and the uncompressed one

            byte[] exch = buf;

            buf = decompressionBuf;

            decompressionBuf = exch;

            return intDecompSize;
        }

        /// <summary>
        /// Locates the bzip2 blocks in the file
        /// </summary>
        private void LocateBlocks()
        {
            ReportProgress(0, IndexingProgress.State.Running, Properties.Resources.ProgressLocatingBlocks);
            FileInfo fi = new FileInfo(filePath);
            bz2_filesize = fi.Length;
            startTime = DateTime.Now;
            elapsed = new TimeSpan(0); 
            bzip2WatchThread = new Thread(BzipBlockLocatorProgressMonitor);
            bzip2WatchThread.Start(); // start the monitor, to report progress

            bzip2.StatusCode status = bzip2.BZ2_bzLocateBlocks(filePath, beginnings, ends, ref totalBlocks, ref bz2_blocks_pct_done);
            bzip2WatchThread.Abort();

            if (status != bzip2.StatusCode.BZ_OK)
                throw new Exception(String.Format(Properties.Resources.FailedLocatingBlocks, filePath, status));

            if (totalBlocks < 1)
                throw new Exception(String.Format(Properties.Resources.NoBlocksFound, filePath));
        }

        /// <summary>
        /// Loads a block from the file, decompresses it and decodes to string
        /// </summary>
        /// <param name="begin">The list of block beginnings</param>
        /// <param name="end">The list of block ends</param>
        /// <returns>The decoded string</returns>
        public string LoadAndDecodeBlock(long[] begin, long[] end)
        {
            byte[] currentBuf = null;

            utf8.Reset();

            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < begin.Length; i++)
            {
                if (blocksCache.ContainsKey(begin[i]))
                {
                    currentBuf = blocksCache[begin[i]];
                }
                else
                {
                    if (blockBuf == null)
                    {
                        blockBuf = new byte[((end[i] - begin[i]) / 8) * 2 + 100];
                    }

                    long loadedLen = LoadBlock(begin[i], end[i], ref blockBuf);

                    currentBuf = new byte[loadedLen];

                    Array.Copy(blockBuf, 0, currentBuf, 0, (int)loadedLen);

                    blocksCache[begin[i]] = currentBuf;
                }

                if (charBuf == null ||
                    charBuf.Length < currentBuf.Length)
                {
                    charBuf = new char[currentBuf.Length];
                }

                int bytesUsed = 0;
                int charsUsed = 0;
                bool completed = false;

                utf8.Convert(currentBuf, 0, currentBuf.Length, charBuf, 0, charBuf.Length, i == begin.Length, out bytesUsed, out charsUsed, out completed);

                sb.Append(charBuf, 0, charsUsed);
            }

            // Purge the cache if needed

            if (blocksCache.Count > MAX_CACHED_BLOCKS_NO)
            {
                List<long> listBegins = new List<long>();

                listBegins.AddRange(blocksCache.Keys);

                Random r = new Random();

                int indexToRemove = r.Next(blocksCache.Count);

                while (blocksCache.Count > MAX_CACHED_BLOCKS_NO)
                {
                    blocksCache.Remove(listBegins[indexToRemove]);

                    listBegins.RemoveAt(indexToRemove);

                    indexToRemove = r.Next(blocksCache.Count);
                }
            }

            return sb.ToString();
        }

        #endregion

        #region Searching methods

        private bool searchRunning;

        private struct SearchItem
        {
            public string SearchRequest;
            public int MaxResults;
            public HitCollection Hits;
            public Queue<Exception> Errors;
        }

        /// <summary>
        /// Searches for the specified term in the index.
        /// </summary>
        /// <param name="term">The term.</param>
        public static HitCollection Search(string term, IEnumerable<Indexer> indexers, int maxResults)
        {
            foreach (Indexer ixr in indexers)
            {
                if (!ixr.indexExists ||
                    ixr.searcher == null)
                {
                    throw new Exception(String.Format(Properties.Resources.IndexDoesNotExist, ixr.filePath));
                }
            }

            string searchRequest = String.Format("title:\"{0}\" AND -\"Image:\"", term);

            HitCollection ret = new HitCollection();

            SearchItem si = new SearchItem();

            si.Hits = ret;
            si.SearchRequest = searchRequest;
            si.MaxResults = maxResults;
            si.Errors = new Queue<Exception>();

            int indexersNumber = 0;

            // I can't really be sure about the thread safety of Lucene

            try
            {
                lock (typeof(Indexer))
                {
                    foreach (Indexer ixr in indexers)
                    {
                        indexersNumber++;

                        int i = 0;

                        while (ixr.searchRunning &&
                            i < 30)
                        {
                            Thread.Sleep(100);

                            i++;
                        }

                        if (i >= 30)
                        {
                            throw new Exception(Properties.Resources.FailedStartingSearch);
                        }

                        ixr.searchRunning = true;
                    }

                    foreach (Indexer ixr in indexers)
                    {
                        ThreadPool.QueueUserWorkItem(ixr.Search, si);
                    }

                    foreach (Indexer ixr in indexers)
                    {
                        int i = 0;

                        while (ixr.searchRunning &&
                            i < 30)
                        {
                            Thread.Sleep(100);

                            i++;
                        }

                        if (i >= 30)
                        {
                            throw new Exception(Properties.Resources.FailedFinishingSearch);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lock (si.Errors)
                {
                    si.Errors.Enqueue(ex);
                }
            }

            StringCollection sc = new StringCollection();

            lock (si.Errors)
            {
                while (si.Errors.Count > 0)
                {
                    Exception ex = si.Errors.Dequeue();

                    if (!sc.Contains(ex.Message))
                    {
                        sc.Add(ex.Message);
                    }
                }
            }

            StringBuilder sb = new StringBuilder();

            foreach (string message in sc)
            {
                sb.AppendLine(message);
            }

            ret.ErrorMessages = sb.Length > 0 ? sb.ToString() : String.Empty;

            if (indexersNumber > 1)
            {
                PageInfo[] arr = new PageInfo[ret.Count];

                for (int i = 0; i < ret.Count; i++)
                {
                    arr[i] = ret[i];
                }

                Array.Sort<PageInfo>(arr,
                    delegate(PageInfo a, PageInfo b)
                    {
                        return (a.Score < b.Score ? 1 : (a.Score > b.Score ? -1 : 0));
                    });

                ret.Clear();

                foreach (PageInfo pi in arr)
                {
                    ret.Add(pi);
                }
            }

            return ret;
        }

        /// <summary>
        /// The searches are always executed on the thread pools
        /// </summary>
        /// <param name="state">The query request</param>
        private void Search(object state)
        {
            SearchItem si = (SearchItem)state;

            try
            {
                Query q = queryParser.Parse(si.SearchRequest);

                IEnumerator hits = searcher.Search(q).Iterator();

                int i = 0;

                while (hits.MoveNext() &&
                    i < si.MaxResults)
                {
                    Hit hit = (Hit)hits.Current;

                    PageInfo pi = new PageInfo(this, hit);

                    lock (si.Hits)
                    {
                        si.Hits.Add(pi);
                    }

                    i++;
                }

                if (i == si.MaxResults &&
                    hits.MoveNext())
                {
                    lock (si.Hits)
                    {
                        si.Hits.HadMoreHits = true;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (si.Errors)
                {
                    si.Errors.Enqueue(ex);
                }
            }

            searchRunning = false;
        }

        #endregion

        #region Misc

        /// <summary>
        /// Closes the searcher object
        /// </summary>
        public void Close()
        {
            if (indexExists &&
                searcher != null)
            {
                searcher.Close();

                searcher = null;
            }
        }

        private void ReportProgress(int percentage, IndexingProgress.State status, string message)
        {
            int eta;
            IndexingProgress ip = new IndexingProgress();

            ip.IndexingState = status;
            ip.Message = message;

            // a naive ETA formula: ETA = ElapsedMinutes * 100 / percentDone - Elapsed
            
            if (percentage > 0)
            {
                elapsed = (DateTime.Now.Subtract(startTime));
                eta = (int)(elapsed.TotalMinutes * 100 / percentage - elapsed.TotalMinutes);       
                
                if (eta <= 0)
                {
                    ip.ETA = Properties.Resources.FinishingSoon;
                }
                else 
                {
                    TimeSpan remaining = TimeSpan.FromMinutes(eta + 1);
                    ip.ETA = String.Format(Properties.Resources.FinishingInHours, (int)remaining.TotalHours, remaining.Minutes);
                }
            }
            else
            {
                ip.ETA = "n/a";
            }
            
            OnProgressChanged(new ProgressChangedEventArgs(percentage, ip));
        }

        public void CreateIndex()
        {
            indexingThread = new Thread(CreateIndexAsync);
            
            indexingThread.Start();
        }

        public void AbortIndex()
        {
            if (indexingThread != null &&
                indexingThread.IsAlive)
            {
                abortIndexing = true;
            }
        }

        protected virtual void OnProgressChanged(ProgressChangedEventArgs e)
        {
            if (ProgressChanged != null)
            {
                ProgressChanged(this, e);
            }
        }

        private Analyzer GuessAnalyzer(string filePath)
        {
            Analyzer ret = null;

            switch (Path.GetFileName(filePath).Substring(0, 2).ToLowerInvariant())
            {
                case "zh":
                    ret = new ChineseAnalyzer();
                    break;
                case "cs":
                    ret = new CzechAnalyzer();
                    break;
                case "da":
                    ret = new SnowballAnalyzer("Danish");
                    break;
                case "nl":
                    ret = new SnowballAnalyzer("Dutch");
                    break;
                case "en":
                    ret = new SnowballAnalyzer("English");
                    break;
                case "fi":
                    ret = new SnowballAnalyzer("Finnish");
                    break;
                case "fr":
                    ret = new SnowballAnalyzer("French");
                    break;
                case "de":
                    ret = new SnowballAnalyzer("German");
                    break;
                case "it":
                    ret = new SnowballAnalyzer("Italian");
                    break;
                case "ja":
                    ret = new CJKAnalyzer();
                    break;
                case "ko":
                    ret = new CJKAnalyzer();
                    break;
                case "no":
                    ret = new SnowballAnalyzer("Norwegian");
                    break;
                case "pt":
                    ret = new SnowballAnalyzer("Portuguese");
                    break;
                case "ru":
                    ret = new SnowballAnalyzer("Russian");
                    break;
                case "es":
                    ret = new SnowballAnalyzer("Spanish");
                    break;
                case "se":
                    ret = new SnowballAnalyzer("Swedish");
                    break;
                default:
                    ret = new StandardAnalyzer();
                    break;
            }

            return ret;
        }

        /// <summary>
        /// The name of the dump file this index is associated
        /// </summary>
        public string File
        {
            get { return filePath.ToLowerInvariant(); }
        }

        #endregion
    }
}
