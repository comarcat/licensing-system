namespace LicensingApi;

/// <summary>
/// The API has no real UI — this only exists because the root "/" is now reachable
/// from the public internet (licensing-api.miautrix.tech) and a bare 404 there looks
/// broken. Redirects to the main site after a short delay.
/// </summary>
public static class RootPage
{
    public const string Html = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>Miautrix Licensing API</title>
            <link rel="preconnect" href="https://fonts.googleapis.com" />
            <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
            <link href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@600;700&family=DM+Sans:wght@400;500&display=swap" rel="stylesheet" />
            <style>
                html, body { height: 100%; margin: 0; }
                body {
                    background: #0a0e1a;
                    position: relative;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    padding: 24px;
                    box-sizing: border-box;
                }
                body::before {
                    content: '';
                    position: fixed;
                    inset: 0;
                    background:
                        radial-gradient(circle at 15% 20%, rgba(99, 102, 241, .22) 0%, transparent 50%),
                        radial-gradient(circle at 85% 80%, rgba(236, 72, 153, .15) 0%, transparent 50%),
                        radial-gradient(circle at 50% 50%, rgba(34, 211, 238, .08) 0%, transparent 60%);
                    z-index: 0;
                    pointer-events: none;
                }
                .card {
                    position: relative;
                    z-index: 1;
                    width: 100%;
                    max-width: 560px;
                    text-align: center;
                    background: linear-gradient(135deg, rgba(255, 255, 255, .06), rgba(255, 255, 255, .02));
                    backdrop-filter: blur(40px);
                    -webkit-backdrop-filter: blur(40px);
                    border: 1px solid rgba(255, 255, 255, .1);
                    border-radius: 28px;
                    padding: 56px 40px;
                }
                .logo { font-size: 2.5rem; margin-bottom: 8px; }
                h1 {
                    font-family: 'Space Grotesk', sans-serif;
                    font-weight: 700;
                    font-size: 2rem;
                    line-height: 1.2;
                    background: linear-gradient(135deg, #fff 0%, #a5b4fc 50%, #ec4899 100%);
                    -webkit-background-clip: text;
                    background-clip: text;
                    -webkit-text-fill-color: transparent;
                    margin: 0 0 16px;
                }
                p {
                    color: rgba(255, 255, 255, .7);
                    font-family: 'DM Sans', sans-serif;
                    font-size: 1rem;
                    max-width: 440px;
                    margin: 0 auto 32px;
                    line-height: 1.6;
                }
                .cta {
                    display: inline-block;
                    background: linear-gradient(135deg, #6366f1, #ec4899);
                    color: #ffffff;
                    text-decoration: none;
                    font-family: 'Space Grotesk', sans-serif;
                    font-weight: 600;
                    font-size: 1rem;
                    padding: 14px 32px;
                    border-radius: 12px;
                    transition: opacity .2s;
                }
                .cta:hover { opacity: .9; }
                .countdown {
                    color: rgba(255, 255, 255, .45);
                    font-family: 'DM Sans', sans-serif;
                    font-size: .85rem;
                    margin: 20px 0 0;
                }
            </style>
        </head>
        <body>
            <main class="card">
                <div class="logo">⚙</div>
                <h1>Miautrix</h1>
                <p>
                    This is an internal Miautrix service (Licensing API). For more
                    information about our products, or to get in touch, visit our
                    main site.
                </p>
                <a class="cta" href="https://miautrix.tech">Contact us →</a>
                <p class="countdown">
                    You will be redirected automatically in <span id="countdown">30</span> seconds…
                </p>
            </main>
            <script>
                (function () {
                    var seconds = 30;
                    var el = document.getElementById('countdown');
                    var timer = setInterval(function () {
                        seconds -= 1;
                        if (el) el.textContent = seconds;
                        if (seconds <= 0) {
                            clearInterval(timer);
                            window.location.href = 'https://miautrix.tech';
                        }
                    }, 1000);
                })();
            </script>
        </body>
        </html>
        """;
}
