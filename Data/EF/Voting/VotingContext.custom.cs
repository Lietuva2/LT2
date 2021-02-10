﻿using System;
using System.Data.Common;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using Framework.Data.Factories;

namespace Data.EF.Voting
{
    public partial class VotingEntities : ICustomDbContext
    {
        public ContextHelper Helper { get; set; }

        protected override void Dispose(bool disposing)
        {
            if (Helper.SaveChangesOnDispose)
            {
                this.SaveChanges();
            }

            if (Helper.DoDispose())
            {
                Helper.IsDisposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
