function handler(event) {
    var response = event.response;
    var cookies = response.cookies;

    if (cookies) {
        for (var cookieName in cookies) {
            // Fix AWSALB and AWSALBCORS cookies by appending Secure if missing
            // This is required because the CF->ALB connection is HTTP, so ALB drops the Secure flag.
            // The browser->CF connection is HTTPS, and SameSite=None requires Secure.
            if (cookieName === 'AWSALB' || cookieName === 'AWSALBCORS' || cookieName === 'AWSALBTG' || cookieName === 'AWSALBTGCORS') {
                var cookie = cookies[cookieName];
                var attributes = cookie.attributes || '';

                // Add Secure attribute if not present
                if (attributes.indexOf('Secure') === -1 && attributes.indexOf('secure') === -1) {
                    cookie.attributes = attributes ? attributes + '; Secure' : 'Secure';
                }
            }
        }
    }

    return response;
}
