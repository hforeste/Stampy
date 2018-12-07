REM fetch changes from given branch and run build
git clean -fdx
git fetch origin %1
git checkout %1
git status
REM resting local branch to remote just in case there were some lingering files somehow
git reset --hard origin/%1
./build-corext.cmd