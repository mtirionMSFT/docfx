﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.DocAsCode.Common;

namespace Microsoft.DocAsCode.Build.Engine;

public interface IInputFileAbstractLayerBuilderProvider
{
    FileAbstractLayerBuilder Create(FileAbstractLayerBuilder defaultBuilder, DocumentBuildParameters parameters);
}
