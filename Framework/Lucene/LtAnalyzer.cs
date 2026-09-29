/**
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Collections;

using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using System.IO;

/**
 * Analyzer for Brazilian language. Supports an external list of stopwords (words that
 * will not be indexed at all) and an external list of exclusions (word that will
 * not be stemmed, but indexed).
 *
 */
namespace Lucene.Net.Analysis.Lt
{
    public sealed class LithuanianAnalyzer : Analyzer
    {

        /**
         * List of typical Brazilian stopwords.
         */
        public static string[] STOP_WORDS = {
      "ir","ar","o","bet", "na", "gi", "nes", "va", "tai"};


        /**
         * Contains the stopwords used with the StopFilter.
         */
        private Hashtable stoptable = new Hashtable();

        /**
         * Contains words that should be indexed but not stemmed.
         */
        private Hashtable excltable = new Hashtable();

        /**
         * Builds an analyzer with the default stop words ({@link #BRAZILIAN_STOP_WORDS}).
         */
        public LithuanianAnalyzer()
        {
            stoptable = StopFilter.MakeStopSet(STOP_WORDS);
        }

        /**
         * Builds an analyzer with the given stop words.
         */
        public LithuanianAnalyzer(string[] stopwords)
        {
            stoptable = StopFilter.MakeStopSet(stopwords);
        }

        /**
         * Builds an analyzer with the given stop words.
         */
        public LithuanianAnalyzer(Hashtable stopwords)
        {
            stoptable = stopwords;
        }

        /**
         * Builds an analyzer with the given stop words.
         */
        public LithuanianAnalyzer(FileInfo stopwords)
        {
            stoptable = WordlistLoader.GetWordSet(stopwords);
        }

        /**
         * Builds an exclusionlist from an array of Strings.
         */
        public void SetStemExclusionTable(string[] exclusionlist)
        {
            excltable = StopFilter.MakeStopSet(exclusionlist);
        }
        /**
         * Builds an exclusionlist from a Hashtable.
         */
        public void SetStemExclusionTable(Hashtable exclusionlist)
        {
            excltable = exclusionlist;
        }
        /**
         * Builds an exclusionlist from the words contained in the given file.
         */
        public void SetStemExclusionTable(FileInfo exclusionlist)
        {
            excltable = WordlistLoader.GetWordSet(exclusionlist);
        }

        /**
         * Creates a TokenStream which tokenizes all the text in the provided Reader.
         *
         * @return  A TokenStream build from a StandardTokenizer filtered with
         * 			StandardFilter, StopFilter, GermanStemFilter and LowerCaseFilter.
         */
        public override TokenStream TokenStream(string fieldName, TextReader reader)
        {
            TokenStream result = new StandardTokenizer(reader);
            result = new LowerCaseFilter(result);
            result = new StandardFilter(result);
            result = new StopFilter(result, stoptable);
            result = new LithuanianStemFilter(result, excltable);
            return result;
        }
    }
}
