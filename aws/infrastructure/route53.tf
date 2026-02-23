# Route53 hosted zone for the apex domain (must already exist in AWS).
# Extract the apex domain from the full domain name, e.g. "capstone.robertb.me" → "robertb.me".
data "aws_route53_zone" "main" {
  count        = var.domain_name != "" ? 1 : 0
  name         = regex("[^.]+\\.[^.]+$", var.domain_name)
  private_zone = false
}

# ACM Certificate for the custom domain.
# CloudFront requires certificates to be in us-east-1.
resource "aws_acm_certificate" "cloudfront" {
  count             = var.domain_name != "" && var.certificate_arn == "" ? 1 : 0
  domain_name       = var.domain_name
  validation_method = "DNS"

  tags = {
    Name = "${var.project_name}-${var.environment}-cloudfront-cert"
  }

  lifecycle {
    create_before_destroy = true
  }
}

# DNS records to validate the ACM certificate via Route53.
resource "aws_route53_record" "cert_validation" {
  for_each = var.domain_name != "" && var.certificate_arn == "" ? {
    for dvo in aws_acm_certificate.cloudfront[0].domain_validation_options : dvo.domain_name => {
      name   = dvo.resource_record_name
      record = dvo.resource_record_value
      type   = dvo.resource_record_type
    }
  } : {}

  allow_overwrite = true
  name            = each.value.name
  records         = [each.value.record]
  ttl             = 60
  type            = each.value.type
  zone_id         = data.aws_route53_zone.main[0].zone_id
}

# Wait for certificate validation to complete before CloudFront uses it.
resource "aws_acm_certificate_validation" "cloudfront" {
  count                   = var.domain_name != "" && var.certificate_arn == "" ? 1 : 0
  certificate_arn         = aws_acm_certificate.cloudfront[0].arn
  validation_record_fqdns = [for record in aws_route53_record.cert_validation : record.fqdn]
}

# Route53 A alias record pointing the custom domain to the CloudFront distribution.
resource "aws_route53_record" "cloudfront_alias" {
  count   = var.domain_name != "" ? 1 : 0
  zone_id = data.aws_route53_zone.main[0].zone_id
  name    = var.domain_name
  type    = "A"

  alias {
    name                   = aws_cloudfront_distribution.frontend.domain_name
    zone_id                = aws_cloudfront_distribution.frontend.hosted_zone_id
    evaluate_target_health = false
  }
}
