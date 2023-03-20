﻿#nullable enable

#if __WASM__ || __MACOS__
#pragma warning disable CS0067, CS0414
#endif

#if XAMARIN || __WASM__ || __SKIA__
using Windows.Foundation;
using Windows.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Uno.Extensions;
using Uno;
using Microsoft.Web.WebView2.Core;
using Uno.UI.Xaml.Controls;

namespace Microsoft.UI.Xaml.Controls;

/// <summary>
/// Represents an object that enables the hosting of web content.
/// </summary>
#if __WASM__ || __SKIA__
[NotImplemented]
#endif
public partial class WebView2 : Control, IWebView
{
	/// <summary>
	/// Initializes a new instance of the WebView2 class.
	/// </summary>
	public WebView2()
	{
		DefaultStyleKey = typeof(WebView2);
		CoreWebView2 = new CoreWebView2(this);
		Loaded += WebView2_Loaded;
	}

	public CoreWebView2 CoreWebView2 { get; }

	protected override void OnApplyTemplate() => CoreWebView2.OnOwnerApplyTemplate();

	private void WebView2_Loaded(object sender, Windows.UI.Xaml.RoutedEventArgs e) =>
		CoreWebView2Initialized?.Invoke(this, new());
}
#endif
