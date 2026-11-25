#!/usr/bin/env bash
# Publish exactly the archives from the build job, never a rebuilt package.
set -euo pipefail
trap 'echo "Preview publication failed at line $LINENO" >&2' ERR

pkg=${1:?archive directory required}
subject=$(git show -s --format=%s "$PRODUCT_SHA")
if [[ ! $subject =~ ^Release\ (v1\.9\.0-preview\.([1-9][0-9]*))$ ]]; then
    echo 'No preview requested by this commit.'; exit 0
fi
tag=${BASH_REMATCH[1]}
number=${BASH_REMATCH[2]}
((number >= 4))
test "$GITHUB_REF" = refs/heads/feature/live-ux-acceptance
test "$(git rev-parse HEAD)" = "$PRODUCT_SHA"
# A retry is allowed only when no tag was created. Never modify an existing tag or release.
if git ls-remote --exit-code origin "refs/tags/$tag" >/dev/null; then
    echo "$tag already exists; refusing to replace it." >&2; exit 1
else
    test "$?" -eq 2
fi
previous="v1.9.0-preview.$((number - 1))"
git ls-remote --exit-code origin "refs/tags/$previous" >/dev/null
test "$(tr -d '\r\n' < "$pkg/SOURCE-SHA.txt")" = "$PRODUCT_SHA"
notes="docs/releases/$tag.md"
test -s "$notes"
git diff-tree --no-commit-id --name-only -r "$PRODUCT_SHA" | grep -Fx "$notes"
(
    cd "$pkg"
    sha256sum -c SHA256SUMS.txt
    mv touch-typing-candidate-linux-x64.tar.gz "touch-typing-$tag-linux-x64.tar.gz"
    mv touch-typing-candidate-windows-x64.zip "touch-typing-$tag-windows-x64.zip"
    tar -tzf "touch-typing-$tag-linux-x64.tar.gz" >/dev/null
    unzip -tq "touch-typing-$tag-windows-x64.zip"
    sha256sum "touch-typing-$tag-linux-x64.tar.gz" "touch-typing-$tag-windows-x64.zip" > SHA256SUMS.txt
)
{
    cat "$notes"
    printf '\n**Коммит:** `%s`\n\n[Проверки архивов, GTK/X11 и Windows Server](%s/%s/actions/runs/%s).\n' "$PRODUCT_SHA" "$GITHUB_SERVER_URL" "$GITHUB_REPOSITORY" "$GITHUB_RUN_ID"
} > "$RUNNER_TEMP/preview-notes.md"
gh release create "$tag" --target "$PRODUCT_SHA" --title "TouchTyping 1.9.0 Preview $number" \
    --prerelease --latest=false --notes-file "$RUNNER_TEMP/preview-notes.md" \
    "$pkg/touch-typing-$tag-linux-x64.tar.gz" "$pkg/touch-typing-$tag-windows-x64.zip" \
    "$pkg/SHA256SUMS.txt" "$pkg/SOURCE-SHA.txt"
# Verify the public downloads, not only the inputs to gh release create.
redownload=$(mktemp -d)
gh release download "$tag" --dir "$redownload"
cmp "$redownload/SHA256SUMS.txt" "$pkg/SHA256SUMS.txt"
cmp "$redownload/SOURCE-SHA.txt" "$pkg/SOURCE-SHA.txt"
(cd "$redownload" && sha256sum -c SHA256SUMS.txt)
test "$(git ls-remote origin "refs/tags/$tag" | cut -f1)" = "$PRODUCT_SHA"
