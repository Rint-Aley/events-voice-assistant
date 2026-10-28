#!/bin/sh
set -eu

case "${AUDIO_RETENTION_DAYS}" in
  ''|0|*[!0-9]*)
    echo "AUDIO_RETENTION_DAYS must be a positive integer, got '${AUDIO_RETENTION_DAYS}'" >&2
    exit 1
    ;;
esac

aws_s3() {
  aws --endpoint-url "${S3_ENDPOINT}" "$@"
}

echo "waiting for S3 at ${S3_ENDPOINT}"
attempt=0
until aws_s3 s3api list-buckets >/dev/null 2>&1; do
  attempt=$((attempt + 1))
  if [ "${attempt}" -ge 60 ]; then
    echo "S3 is not reachable at ${S3_ENDPOINT}" >&2
    exit 1
  fi
  sleep 1
done

for bucket in "${S3_BUCKET_AUDIO}" "${S3_BUCKET_TTS}"; do
  if aws_s3 s3api head-bucket --bucket "${bucket}" >/dev/null 2>&1; then
    echo "bucket ${bucket}: exists"
  else
    aws_s3 s3api create-bucket --bucket "${bucket}" >/dev/null
    echo "bucket ${bucket}: created"
  fi

  aws_s3 s3api put-public-access-block \
    --bucket "${bucket}" \
    --public-access-block-configuration \
    "BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true" \
    >/dev/null
  echo "bucket ${bucket}: private"
done

cat >/tmp/lifecycle.json <<EOF
{
  "Rules": [
    {
      "ID": "audio-expire-${AUDIO_RETENTION_DAYS}d",
      "Status": "Enabled",
      "Filter": { "Prefix": "" },
      "Expiration": { "Days": ${AUDIO_RETENTION_DAYS} }
    }
  ]
}
EOF

aws_s3 s3api put-bucket-lifecycle-configuration \
  --bucket "${S3_BUCKET_AUDIO}" \
  --lifecycle-configuration file:///tmp/lifecycle.json \
  >/dev/null
echo "bucket ${S3_BUCKET_AUDIO}: lifecycle ${AUDIO_RETENTION_DAYS}d"

echo "buckets:"
aws_s3 s3api list-buckets --query "Buckets[].Name" --output text
