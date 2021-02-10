using System;
using System.Linq;
using System.Text;
using Data.Enums;
using Framework.Infrastructure.Storage;

namespace Data.MongoDB
{
    public class Settings
    {
        private string _supportedIdeaText;
        private string _votedText;

        public MongoObjectId LanguageId { get; set; }
        public UserVisibility Visibility { get; set; }
        public UserDetailsVisibility DetailsVisibility { get; set; }
        public bool VotesArePublic { get; set; }
        public NewsLetterFrequency NewsLetterFrequency { get; set; }

        public string SupportedIdeaText
        {
            get
            {
                if (string.IsNullOrEmpty(_supportedIdeaText))
                {
                    _supportedIdeaText = "Geras sumanymas!";
                }

                return _supportedIdeaText;
            }
            set { _supportedIdeaText = value; }
        }

        public string VotedText
        {
            get
            {
                if (string.IsNullOrEmpty(_votedText))
                {
                    _votedText = "Aktualus balsavimas!";
                }

                return _votedText;
            }
            set { _votedText = value; }
        }

        public Settings()
        {
            DetailsVisibility = new UserDetailsVisibility();
            VotesArePublic = true;
            NewsLetterFrequency = NewsLetterFrequency.Monthly;
        }
    }
}
