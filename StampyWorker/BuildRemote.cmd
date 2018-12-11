REM fetch changes from given branch and run build
git fetch origin %1
git checkout %1
REM resting local branch to remote just in case there were some lingering files somehow
git reset --hard origin/%1
call build-corext.cmd
git checkout dev
git branch -D %1