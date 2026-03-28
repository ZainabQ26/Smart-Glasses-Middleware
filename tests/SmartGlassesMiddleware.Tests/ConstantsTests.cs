using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SmartGlassesMiddleware.Controllers;
using SmartGlassesMiddleware.Models;
using SmartGlassesMiddleware.Services;

namespace SmartGlassesMiddleware.Tests.Controllers;

[TestFixture]
public class ConstantsTests
{
    [Test]
    public void PdfConstants_Check()
    {
        Assert.That(PdfConstants.PageWidth, Is.GreaterThan(0));
        Assert.That(PdfConstants.PageHeight, Is.GreaterThan(0));
    }
}
