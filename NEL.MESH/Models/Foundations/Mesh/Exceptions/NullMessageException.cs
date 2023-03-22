﻿// ---------------------------------------------------------------
// Copyright (c) North East London ICB. All rights reserved.
// ---------------------------------------------------------------

using Xeptions;

namespace NEL.MESH.Models.Foundations.Mesh.Exceptions
{
    internal class NullMessageException : Xeption
    {
        public NullMessageException()
            : base(message: "Message is null.") { }
    }
}
