namespace Lucene.Net.Analysis.Lt
{

    public class LithuanianStemmer
    {
        public string Stem(string term)
        {
            return changeTerm(term);
        }
        
        private string changeTerm(string value)
        {
            int j;
            string r = "";

            // be-safe !!!
            if (value == null)
            {
                return null;
            }

            value = value.ToLower();
            for (j = 0; j < value.Length; j++)
            {
                if ((value[j] == 'ą'))
                {
                    r = r + "a"; continue;
                }
                if ((value[j] == 'ę') ||
                    (value[j] == 'ė'))
                {
                    r = r + "e"; continue;
                }
                if (value[j] == 'į')
                {
                    r = r + "i"; continue;
                }
                if ((value[j] == 'ų') ||
                    (value[j] == 'ū'))
                {
                    r = r + "u"; continue;
                }
                if ((value[j] == 'č'))
                {
                    r = r + "c"; continue;
                }
                if (value[j] == 'š')
                {
                    r = r + "s"; continue;
                }
                if (value[j] == 'ž')
                {
                    r = r + "z"; continue;
                }

                r = r + value[j];
            }

            return r;
        }
    }
}