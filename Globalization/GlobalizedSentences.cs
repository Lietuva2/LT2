using System;
using Globalization.Resources.Services;

namespace Globalization
{
    public static class GlobalizedSentences
    {
        public static string GetVotesCountString(int number)
        {
            return string.Format(GetCase(number, Votes.None, Votes.Teen,
                           Votes.Single, Votes.Many), number);
        }

        public static string GetCommentsCountString(int number)
        {
            return string.Format(GetCase(number, Comments.None, Comments.Teen,
                           Comments.Single, Comments.Many), number);
        }

        public static string GetCommentsWhatString(int number)
        {
            return string.Format(GetCase(number, Comments.None, Comments.Teen,
                           Comments.SingleWhat, Comments.ManyWhat), number);
        }

        public static string GetViewsCountString(int number)
        {
            return string.Format(GetCase(number, Views.None, Views.Teen,
                           Views.Single, Views.Many), number);
        }

        public static object GetUsersString(int number)
        {
            return string.Format(GetCase(number, Users.NoUsers, Users.TeenUsers,
                           Users.SingleUser, Users.ManyUsers), number);
        }

        public static string GetNewsCountText(int number)
        {
            return GetCase(number, NewsCount.No, NewsCount.Teen,
                           NewsCount.Single, NewsCount.Many);
        }

        public static string GetSupportingUsersString(int number)
        {
            return string.Format(GetCase(number, SupportingUsers.NoSupporters, SupportingUsers.TeenSupporters,
                           SupportingUsers.SingleSupporter, SupportingUsers.ManySupporters), number);
        }

        public static string GetNumberOfTimesString(int number)
        {
            return string.Format(GetCase(number, NumberOfTimes.Never, NumberOfTimes.TeenTimes,
                           NumberOfTimes.SingleTime, NumberOfTimes.ManyTimes), number);
        }

        public static string GetNumberOfIdeasString(int number)
        {
            return string.Format(GetCase(number, NumberOfTimes.NoIdeas, NumberOfTimes.TeenIdeas,
                           NumberOfTimes.SingleIdea, NumberOfTimes.ManyIdeas), number);
        }

        public static string GetNumberOfSolutionsString(int number)
        {
            return string.Format(GetCase(number, NumberOfTimes.NoSolutions, NumberOfTimes.TeenSolutions,
                           NumberOfTimes.SingleSolution, NumberOfTimes.ManySolutions), number);
        }

        public static string GetNumberOfCategoriesString(int number)
        {
            return string.Format(GetCase(number, NumberOfTimes.NoCategories, NumberOfTimes.TeenCategories,
                           NumberOfTimes.SingleCategory, NumberOfTimes.ManyCategories), number);
        }

        public static string GetNumberOfIssuesString(int number)
        {
            return string.Format(GetCase(number, NumberOfTimes.NoIssues, NumberOfTimes.TeenIssues,
                           NumberOfTimes.SingleIssue, NumberOfTimes.ManyIssues), number);
        }
        
        public static string GetNumberOfProjectsString(int number)
        {
            return string.Format(GetCase(number, NumberOfTimes.NoProjects, NumberOfTimes.TeenProjects,
                           NumberOfTimes.SingleProject, NumberOfTimes.ManyProjects), number);
        }

        public static string GetNumberOfProblemsString(int number)
        {
            return string.Format(GetCase(number, NumberOfTimes.NoProblems, NumberOfTimes.TeenProblems,
                           NumberOfTimes.SingleProblem, NumberOfTimes.ManyProblems), number);
        }

        public static string GetMembersCountString(int number)
        {
            return GetCase(number, NumberOfTimes.NoMembers, NumberOfTimes.TeenMembers,
                           NumberOfTimes.SingleMember, NumberOfTimes.ManyMembers);
        }

        public static string GetTimeLeftString(DateTime? date)
        {
            if(!date.HasValue)
            {
                return string.Empty;
            }

            var time = date - DateTime.Now;
            return GetTimeString(time.Value);
        }

        public static string GetTimePassedString(DateTime? date)
        {
            if (!date.HasValue)
            {
                return string.Empty;
            }

            var time = DateTime.Now - date.Value;
            return GetTimeString(time);
        }

        private static string GetTimeString(TimeSpan time)
        {
            if (time.Days > 30)
            {
                return string.Format(GetCase(time.Days / 30, string.Empty, Time.TeenMonth, Time.SingleMonth, Time.ManyMonths), time.Days / 30) + " " +
                    string.Format(GetCase(time.Days, string.Empty, Time.TeenDays, Time.SingleDay, Time.ManyDays), time.Days % 30);
            }

            if (time.Days > 0)
            {
                return string.Format(GetCase(time.Days, string.Empty, Time.TeenDays, Time.SingleDay, Time.ManyDays), time.Days) + " " +
                    string.Format(GetCase(time.Hours, string.Empty, Time.TeenHours, Time.SingleHour, Time.ManyHours), time.Hours);
            }

            return string.Format(GetCase(time.Hours, string.Empty, Time.TeenHours, Time.SingleHour, Time.ManyHours), time.Hours) + " " + 
                   string.Format(GetCase(time.Minutes, Time.LessThanMinute, Time.TeenMinutes, Time.SingleMinute, Time.ManyMinutes), time.Minutes);
        }

        private static string GetCase(int number, string zero, string teen, string single, string many)
        {
            if (number == 0)
            {
                return zero;
            }

            if(number % 10 == 0)
            {
                return teen;
            }

            if (number % 100 > 10 && number % 100 < 20)
            {
                return teen;
            }

            if (number % 10 == 1)
            {
                return single;
            }

            return many;
        }

        public static string GetActionDescription(string name)
        {
            return ActionDescriptions.ResourceManager.GetString(name);
        }
    }
}